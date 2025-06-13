using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PreviousDevelopmentToRefactor.Cropper;
using PreviousDevelopmentToRefactor.Environments;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace PreviousDevelopmentToRefactor.LibsOfDDNCrop
{
    public class CommandTransBaseOfDDNCrop : CommandTransBase
    {
        public CommandTransBaseOfDDNCrop(Transaction acTrans) : base(acTrans)
        {
        }

        public override bool Run()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            Curve boundary;
            IEnumerable<ObjectId> objectIds;
            //if (!SelectionCrop(out boundary, out objectIds)) return false;

            const string dmtzLayer = "DMTZ";

            //地貌特征线Id
            var dmtzIds = GetLayeredPolylineIds(dmtzLayer);
            if (!dmtzIds.Any())
            {
                ed.WriteMessage("\n模型中无地貌特征多段线");
                return false;
            }
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var boundarys = new List<Curve>();
            foreach (var id in dmtzIds)
            {
                var itemBoundary = AcTrans.GetObjectForRead(id) as Curve;
                if (itemBoundary == null || !itemBoundary.Closed) continue;

                boundarys.Add(itemBoundary);
            }
            if (!boundarys.Any())
            {
                ed.WriteMessage("\n模型中无闭合的地貌特征多段线");
                return false;
            }

            Matrix3d OLD = ed.CurrentUserCoordinateSystem;
            ed.CurrentUserCoordinateSystem = Matrix3d.Identity;

            try
            {
                foreach (var itemBoundary in boundarys)
                {
                    Matrix3d ecs = itemBoundary.Ecs;

                    boundary = itemBoundary;
                    //Note 直接落在范围内的直接删除
                    objectIds = FindIntersectingEntitiesAdvanced(boundary, db, out List<ObjectId> insideIds, out var intersectionDic, boundarys);
                    foreach (var id in insideIds)
                    {
                        if (id == null || id == boundary.Id) continue;

                        var itemInsideEnt = AcTrans.GetObjectForWrite(id) as Entity;
                        if (itemInsideEnt == null) continue;

                        itemInsideEnt?.Erase();
                    }

                    var result = CropEntitiesWithBoundary(objectIds, boundary, WhichSideToKeep.Outside, intersectionDic);
                }
                sw.Stop();
                long time = sw.ElapsedMilliseconds;
                ed.WriteMessage("\nDMTZ图层共有闭合曲线{0}个，删除所覆盖的全部实体\n合计:{1,13}秒", boundarys.Count, time / 1000);
                // CurEditorHelper.WriteMessage(result.ToString());
                return true;
            }
            catch (System.Exception ex)
            {
                AcAp.ShowAlertDialog(string.Format("\nError: {0}\nStackTrace: {1}", ex.Message, ex.StackTrace));
                return false;
            }
            finally { ed.CurrentUserCoordinateSystem = OLD; }
        }
        /// <summary>
        /// 获取指定图层的多段线
        /// </summary>
        /// <returns></returns>
        public List<ObjectId> GetLayeredPolylineIds(string layerName)
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var tvs = new TypedValue[]
            {
                new TypedValue((int)DxfCode.Operator,"<and"),
                    new TypedValue((int)DxfCode.LayerName,layerName),
                    new TypedValue((int)DxfCode.Start,"*Polyline"),
                new TypedValue((int)DxfCode.Operator,"and>"),
            };

            var sf = new SelectionFilter(tvs);
            PromptSelectionResult psr = ed.SelectAll(sf);

            if (psr.Status == PromptStatus.OK)
                return psr.Value.GetObjectIds().ToList();

            return new List<ObjectId>();
        }

        /// <summary>
        /// 选择剪裁
        /// </summary>
        /// <param name="boundary"></param>
        /// <param name="objectIds"></param>
        /// <returns></returns>
        private bool SelectionCrop(out Curve boundary, out IEnumerable<ObjectId> objectIds)
        {
            objectIds = new HashSet<ObjectId>();
            boundary = null;

            var sset = CurEditorHelper.GetSelection("\nSelect entities to be cropped:");
            if (sset == null) return false;
            while (true)
            {
                var boundaryId = CurEditorHelper.GetEntity("\nSelect a closed Curve as crop boundary: ",
                    "Invalid selection: requires a closed Curve", typeof(Curve));
                if (boundaryId == ObjectId.Null) return false;

                boundary = AcTrans.GetObjectForRead(boundaryId) as Curve;
                ;
                if (boundary.Closed) break;
                CurEditorHelper.WriteMessage("\nPolyline should be closed.");
            }

            objectIds = sset.GetObjectIds();
            return true;
        }

        public List<ObjectId> FindIntersectingEntitiesAdvanced(Curve curve,
                                                               Database db,
                                                               out List<ObjectId> insideIds,
                                                               out Dictionary<ObjectId, List<Point3d>> intersectionDic,
                                                               List<Curve> excludeBoundarys = null)
        {
            insideIds = new List<ObjectId>();
            intersectionDic = new Dictionary<ObjectId, List<Point3d>>();
            var intersectingEntities = new List<ObjectId>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 获取模型空间
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                var modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                // 获取多段线的边界
                Extents3d polyExtents = curve.GeometricExtents;

                // 遍历模型空间中的所有实体
                foreach (ObjectId objId in modelSpace)
                {
                    if (objId == ObjectId.Null || objId == curve.Id) continue;
                    if (excludeBoundarys != null && excludeBoundarys.Any(boundary => objId == boundary.Id)) continue;

                    //if (!objId.Handle.ToString().Equals("18F26")) continue;

                    Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (ent != null && ent is Curve curve1)
                    {
                        if (polyExtents.Contains(ent.GeometricExtents))
                        {
                            insideIds.Add(objId);
                            continue;
                        }
                        // 检查边界框是否相交
                        if (DoExtentsIntersect(ent.GeometricExtents, polyExtents))
                        {
                            //需要作局部坐标调整，否则求交不准.使用ECS坐标系计算器
                            var calculator = new ECSIntersectionCalculator(curve);

                            var intersections = calculator.FindIntersectionsInEcsCoordinates(curve, curve1);

                            //// 输出结果
                            //Point3dCollection intersections = new Point3dCollection();
                            //curve.IntersectWith(ent, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);

                            if (intersections.Count > 0)
                            {
                                intersectingEntities.Add(objId);
                                intersectionDic.Add(objId, intersections);
                            }
                        }
                    }
                }
                tr.Commit();
            }

            return intersectingEntities;
        }


        // 检查两个边界框是否相交
        private bool DoExtentsIntersect(Extents3d ext1, Extents3d ext2)
        {
            return !(ext1.MaxPoint.X < ext2.MinPoint.X || ext1.MinPoint.X > ext2.MaxPoint.X ||
                     ext1.MaxPoint.Y < ext2.MinPoint.Y || ext1.MinPoint.Y > ext2.MaxPoint.Y ||
                     ext1.MaxPoint.Z < ext2.MinPoint.Z || ext1.MinPoint.Z > ext2.MaxPoint.Z);
        }
    }
    public static class Extents3dExtend
    {
        /// <summary>
        /// 判断一个BoundingBox3D是否完全包含另一个BoundingBox3D
        /// </summary>
        public static bool Contains(this Extents3d container, Extents3d containee)
        {
            if (container == null || containee == null)
                return false;

            // 检查containee的所有角点是否都在container内
            return container.Contains(containee.MinPoint) &&
                   container.Contains(containee.MaxPoint);
        }
        /// <summary>
        /// 判断一个点是否在BoundingBox3D内
        /// </summary>
        public static bool Contains(this Extents3d bbox, Point3d point)
        {
            if (bbox == null || point == null)
                return false;

            return point.X >= bbox.MinPoint.X && point.X <= bbox.MaxPoint.X &&
                   point.Y >= bbox.MinPoint.Y && point.Y <= bbox.MaxPoint.Y &&
                   point.Z >= bbox.MinPoint.Z && point.Z <= bbox.MaxPoint.Z;
        }
    }

    public class ECSIntersectionCalculator
    {
        // 定义转换矩阵
        private Matrix3d _globalToEcs;
        private Matrix3d _ecsToGlobal;

        // 构造函数，初始化ECS坐标系
        public ECSIntersectionCalculator(Curve baseCurve)
        {
            // 创建全局到ECS的转换矩阵
            var pt = baseCurve.StartPoint.GetMidBetween2Pt(baseCurve.EndPoint);
            _globalToEcs = baseCurve.Ecs.PostMultiplyBy(Matrix3d.Displacement(pt.GetVectorTo(Point3d.Origin)));

            // 创建ECS到全局的转换矩阵
            _ecsToGlobal = _globalToEcs.Inverse();
        }

        // 将全局坐标转换为ECS坐标
        public Point3d GlobalToEcs(Point3d globalPoint)
        {
            return globalPoint.TransformBy(_globalToEcs);
        }

        // 将ECS坐标转换为全局坐标
        public Point3d EcsToGlobal(Point3d ecsPoint)
        {
            return ecsPoint.TransformBy(_ecsToGlobal);
        }

        // 转换曲线到ECS坐标系
        public Curve TransformToEcs(Curve globalCurve)
        {
            Curve ecsCurve = globalCurve.Clone() as Curve;
            ecsCurve.TransformBy(_globalToEcs);
            return ecsCurve;
        }

        // 转换曲线到全局坐标系
        public Curve TransformToGlobal(Curve ecsCurve)
        {
            Curve globalCurve = ecsCurve.Clone() as Curve;
            globalCurve.TransformBy(_ecsToGlobal);
            return globalCurve;
        }

        // 在ECS坐标系中计算两条曲线的交点
        public List<Point3d> FindIntersectionsInEcsCoordinates(Curve curve1, Curve curve2)
        {
            List<Point3d> intersections = new List<Point3d>();

            // 转换到ECS坐标系
            using (Curve ecsCurve1 = TransformToEcs(curve1))
            using (Curve ecsCurve2 = TransformToEcs(curve2))
            {
                // 创建交点集合
                Point3dCollection ecsIntersections = new Point3dCollection();

                // 在ECS坐标系中计算交点
                ecsCurve1.IntersectWith(ecsCurve2, Intersect.OnBothOperands, ecsIntersections, IntPtr.Zero, IntPtr.Zero);

                // 转换回全局坐标系
                foreach (Point3d ecsPoint in ecsIntersections)
                {
                    intersections.Add(EcsToGlobal(ecsPoint));
                }
            }

            return intersections;
        }
    }
}