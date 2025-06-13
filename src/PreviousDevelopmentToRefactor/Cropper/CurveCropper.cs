using System.Collections.Generic;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PreviousDevelopmentToRefactor.Environments;

namespace PreviousDevelopmentToRefactor.Cropper
{
    /// <summary>
    /// 曲线裁剪器
    /// </summary>
    internal class CurveCropper : EntityCropper<Curve>
    {
        public CurveCropper(Curve entity, Curve boundary, WhichSideToKeep whichSideToKeep, CommandTransBase commandTransBase, List<Point3d> intersects = null)
            : base(entity, boundary, whichSideToKeep, commandTransBase, intersects)
        {
        }

        internal override IEnumerable<ObjectId> Trim()
        {
            using (var objs = _entity.GetSortedSplitCurves(_ptIntersects))
            {
                var segmentsToKeep = new List<Curve>();
                var segmentIdsToKeep = new List<ObjectId>();
                foreach (var obj in objs)
                {
                    var curve = obj as Curve;
                    try
                    {
                        var midPt = curve.GetCurveMidPoint();
                        if (_boundary.GetPointContainment(midPt) == PointContainment.OnBoundary ||
                            _boundary.GetPointContainment(midPt).ToString() == _whichSideToKeepString)
                        {
                            segmentsToKeep.Add(curve);
                            segmentIdsToKeep.Add(curve.Id);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        continue;
                    }
                }

                if (segmentsToKeep.Count == objs.Count) return new List<ObjectId> { _entity.Id };
                var blockId = _entity.BlockId;
                _commandTransBase.GetObjectForWrite(_entity.Id);
                _entity.Erase(true);
                if (segmentsToKeep.Count == 0) return new List<ObjectId>();
                _commandTransBase.AddEntsToBlockTableRecord(segmentsToKeep, blockId);
                return segmentIdsToKeep;
            }
        }

        internal override Point3d GetPosition()
        {
            return _entity.StartPoint;
        }
    }
}