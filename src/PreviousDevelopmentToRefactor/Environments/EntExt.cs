using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Face = Autodesk.AutoCAD.BoundaryRepresentation.Face;

namespace PreviousDevelopmentToRefactor.Environments
{
    public enum EntContainment
    {
        Outside,
        Inside,
        MayIntersect
    }

    public static class EntExt
    {
        public static PointContainment GetPointContainment(Region region, Point3d point)
        {
            var result = PointContainment.Outside;

            /// Get a Brep object representing the region:
            using (var brep = new Brep(region))
            {
                if (brep != null)
                    // Get the PointContainment and the BrepEntity at the given point:
                    using (var ent = brep.GetPointContainment(point, out result))
                    {
                        /// GetPointContainment() returns PointContainment.OnBoundary
                        /// when the picked point is either inside the region's area
                        /// or exactly on an edge.
                        /// 
                        /// So, to distinguish between a point on an edge and a point
                        /// inside the region, we must check the type of the returned
                        /// BrepEntity:
                        /// 
                        /// If the picked point was on an edge, the returned BrepEntity 
                        /// will be an Edge object. If the point was inside the boundary,
                        /// the returned BrepEntity will be a Face object.
                        //
                        /// So if the returned BrepEntity's type is a Face, we return
                        /// PointContainment.Inside:
                        if (ent is Face)
                            result = PointContainment.Inside;
                    }
            }

            return result;
        }

        public static Region RegionFromClosedCurve(Curve curve)
        {
            if (!curve.Closed)
                throw new ArgumentException("Curve must be closed.");
            var curves = new DBObjectCollection();
            curves.Add(curve);
            using (var regions = Region.CreateFromCurves(curves))
            {
                if (regions == null || regions.Count == 0)
                    throw new InvalidOperationException("Failed to create regions");
                if (regions.Count > 1)
                    throw new InvalidOperationException("Multiple regions created");
                return regions.Cast<Region>().First();
            }
        }

        public static PointContainment GetPointContainment(this Curve curve, Point3d point)
        {
            if (!curve.Closed)
                throw new ArgumentException("Curve must be closed.");
            var region = RegionFromClosedCurve(curve);
            if (region == null)
                throw new InvalidOperationException("Failed to create region");
            using (region)
            {
                return GetPointContainment(region, point);
            }
        }

        public static double GetCurveLength(this Curve curve)
        {
            return curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
        }

        public static Point3d GetCurveMidPoint(this Curve curve)
        {
            try
            {
                return curve.GetPointAtParameter((curve.EndParam + curve.StartParam) / 2);
            }
            catch (System.Exception ex)
            {
                return curve.GetPointAtParameter(0.5);
            }
        }

        public static DBObjectCollection GetSortedSplitCurves(this Curve curve, Point3dCollection pt3dCollection)
        {
            try
            {
                var ptCount = pt3dCollection.Count;
                var paraArr = new double[ptCount];
                for (var i = 0; i < ptCount; i++) paraArr[i] = curve.GetParameterAtPoint(curve.GetClosestPointTo(pt3dCollection[i], false));

                Array.Sort(paraArr);
                return curve.GetSplitCurves(new DoubleCollection(paraArr));
            }
            catch (System.Exception ex)
            {
                return new DBObjectCollection();
            }
        }
        //        public static DBObjectCollection GetSortedSplitCurves(this Spline spline, Point3dCollection pt3dCollection)
        //        {
        //            Curve curve = spline.ToPolyline();
        //            DBObjectCollection result = curve.GetSortedSplitCurves(pt3dCollection);
        //            curve.Dispose();
        //            return result;
        //        }

        public static Point3dCollection IntersectWith(this Entity ent1, Entity ent2)
        {
            var ptInts = new Point3dCollection();
            ent1.IntersectWith(ent2, Intersect.OnBothOperands, ent1.GetPlane(), ptInts, IntPtr.Zero,
                IntPtr.Zero);
            return ptInts;
        }

        public static Point3d GetMidBetween2Pt(this Point3d pt1, Point3d pt2)
        {
            var arr1 = pt1.ToArray();
            var arr2 = pt2.ToArray();
            var arrResult = new double[3];
            for (var i = 0; i <= 2; i++) arrResult[i] = (arr1[i] + arr2[i]) / 2;

            return new Point3d(arrResult);
        }

        public static Point3d GetGeoExtentsCen(this Entity ent)
        {
            var extents3d = ent.GeometricExtents;
            return extents3d.MinPoint.GetMidBetween2Pt(extents3d.MaxPoint);
        }

        public static Point3d ToPoint3d(this Point2d point2d)
        {
            return new Point3d(point2d.X, point2d.Y, 0);
        }

        public static IEnumerable<ObjectId> GenerateHatchBoundaries(this Hatch hatch, CommandTransBase cmdTransBase)
        {
            var boundaryIds = new List<ObjectId>();
            var ownerId = hatch.OwnerId;
            var plane = hatch.GetPlane();
            var nLoops = hatch.NumberOfLoops;
            for (var i = 0; i < nLoops; i++)
            {
                var loop = hatch.GetLoopAt(i);
                if (loop.IsPolyline)
                {
                    var bulgeVertexCollection = loop.Polyline;
                    boundaryIds.Add(bulgeVertexCollection.BulgeVertexCollectionToPolyline(ownerId, cmdTransBase));
                    continue;
                }

                foreach (Curve2d cv in loop.Curves) boundaryIds.Add(cv.Curve2DToDbCurve(plane, ownerId, cmdTransBase));
            }

            return boundaryIds;
        }

        public static string GetHatchLoopTypes(this Hatch hatch)
        {
            var prompt = $"\n{hatch.Id.ToString()}:";
            var hatchNumberOfLoops = hatch.NumberOfLoops;
            for (var i = 0; i < hatchNumberOfLoops; i++)
            {
                var loopTypeString = hatch.GetLoopAt(i).LoopType.ToString();
                prompt += $"{i.ToString()}:{loopTypeString};";
            }

            return prompt;
        }
    }
}