using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PreviousDevelopmentToRefactor.Environments;

namespace PreviousDevelopmentToRefactor.Cropper
{
    /// <summary>
    /// 剪裁边保留枚举项
    /// </summary>
    public enum WhichSideToKeep
    {
        /// <summary>
        /// 外部
        /// </summary>
        Outside,
        /// <summary>
        /// 内侧
        /// </summary>
        Inside
    }

    /// <summary>
    /// 对象剪裁接口
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface EntityCropperInterface<out T> where T : Entity
    {
        /// <summary>
        /// 剪裁
        /// </summary>
        /// <returns></returns>
        IEnumerable<ObjectId> Crop();
    }

    public class EntityCropper<T> : EntityCropperInterface<T> where T : Entity
    {
        internal Curve _boundary;
        internal CommandTransBase _commandTransBase;
        internal T _entity;
        internal Point3dCollection _ptIntersects;
        internal WhichSideToKeep _whichSideToKeep;
        internal string _whichSideToKeepString;

        public EntityCropper(T entity, Curve boundary, WhichSideToKeep whichSideToKeep, CommandTransBase commandTransBase, List<Point3d> intersects = null)
        {
            _entity = entity;
            _boundary = boundary;
            _whichSideToKeep = whichSideToKeep;
            _whichSideToKeepString = whichSideToKeep.ToString();
            _commandTransBase = commandTransBase;
            if (intersects != null) _ptIntersects = new Point3dCollection(intersects.ToArray());
        }

        public virtual IEnumerable<ObjectId> Crop()
        {
            if (_ptIntersects == null || _ptIntersects.Count == 0) _ptIntersects = _boundary.IntersectWith(_entity);
            //            if(!(_entity is BlockReference))_commandTransBase.AddPointsToBlockTableRecord(_ptIntersects,_entity.BlockId);
            IEnumerable<ObjectId> result = new List<ObjectId>();
            if (_ptIntersects.Count == 0)
                return result.Concat(CheckPosition());
            return result.Concat(Trim());
        }

        public static EntityCropperInterface<T> NewEntityCropper(T entity, Curve boundary, WhichSideToKeep whichSideToKeep, CommandTransBase commandTransBase, Dictionary<ObjectId, List<Point3d>> intersectionDic = null)
        {
            var intersects = new List<Point3d>();
            if (intersectionDic != null && intersectionDic.ContainsKey(entity.Id)) intersects = intersectionDic[entity.Id];

            if (entity is BlockReference)
                return new BlockReferenceCropper(entity as BlockReference, boundary, whichSideToKeep, commandTransBase)
                    as EntityCropperInterface<T>;
            if (entity is Curve)
                return new CurveCropper(entity as Curve, boundary, whichSideToKeep, commandTransBase, intersects) as
                    EntityCropperInterface<T>;
            if (entity is DBPoint)
                return new DbPointCropper(entity as DBPoint, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is DBText)
                return new DbTextCropper(entity as DBText, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Dimension)
                return new DimCropper(entity as Dimension, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Face)
                return new FaceCropper(entity as Face, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Hatch)
                return new HatchCropper(entity as Hatch, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is MLeader)
                return new MLeaderCropper(entity as MLeader, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Mline)
                return new MLineCropper(entity as Mline, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is MText)
                return new MTextCropper(entity as MText, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Region)
                return new RegionCropper(entity as Region, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Shape)
                return new ShapeCropper(entity as Shape, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            if (entity is Solid)
                return new SolidCropper(entity as Solid, boundary, whichSideToKeep, commandTransBase) as
                    EntityCropperInterface<T>;
            return new EntityCropper<T>(entity, boundary, whichSideToKeep, commandTransBase);
        }

        internal virtual IEnumerable<ObjectId> Trim()
        {
            return new List<ObjectId>();
        }

        internal IEnumerable<ObjectId> CheckPosition()
        {
            var containmentString = _boundary.GetPointContainment(GetPosition()).ToString();

            if (containmentString == _whichSideToKeepString) return new List<ObjectId> { _entity.Id };
            _commandTransBase.GetObjectForWrite(_entity.Id);
            _entity.Erase();
            return new List<ObjectId>();
        }

        internal virtual Point3d GetPosition()
        {
            return _entity.GetGeoExtentsCen();
        }
    }
}