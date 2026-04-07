using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;

namespace GetPropsTool
{
    public static class CreateBoundary
    {
        public const string RegAppName = "MCG_PLATE_DATA";
        public const string TargetLayer = "Mechanical-AM_5";
        public const string BaseCogLayer = "Mechanical-AM_8";
        public const string CogBlockName = "COG Block";
        public const string BaseCogBlockName = "BaseCOG_Block";

        public static void EnsureLayerExists(Transaction tr, Database db, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord { Name = layerName, Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex) };
                lt.Add(ltr); 
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        public static void CreateMText(Transaction tr, BlockTableRecord btr, Point3d loc, string txt, double height)
        {
            EnsureLayerExists(tr, db: tr.GetObject(btr.OwnerId, OpenMode.ForRead).Database, TargetLayer, 4);
            MText mt = new MText { Contents = txt, Location = loc, TextHeight = height, Attachment = AttachmentPoint.MiddleCenter, Layer = TargetLayer, ColorIndex = 2 };
            btr.AppendEntity(mt); 
            tr.AddNewlyCreatedDBObject(mt, true);
        }

        public static void InsertCogBlock(Transaction tr, Database db, BlockTableRecord btr, Point3d loc, double scale)
        {
            EnsureLayerExists(tr, db, TargetLayer, 4);
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(CogBlockName))
            {
                bt.UpgradeOpen();
                BlockTableRecord newBtr = new BlockTableRecord { Name = CogBlockName };
                Circle c = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.5);
                Line l1 = new Line(new Point3d(-0.8, 0, 0), new Point3d(0.8, 0, 0));
                Line l2 = new Line(new Point3d(0, -0.8, 0), new Point3d(0, 0.8, 0));
                newBtr.AppendEntity(c); newBtr.AppendEntity(l1); newBtr.AppendEntity(l2);
                bt.Add(newBtr); 
                tr.AddNewlyCreatedDBObject(newBtr, true);
            }
            BlockReference bref = new BlockReference(loc, bt[CogBlockName]) { ScaleFactors = new Scale3d(scale), Layer = TargetLayer };
            btr.AppendEntity(bref); 
            tr.AddNewlyCreatedDBObject(bref, true);
        }

        // HÀM MỚI: Tạo Block BaseCOG (Gốc CNC)
        public static void InsertBaseCogBlock(Transaction tr, Database db, BlockTableRecord btr, Point3d loc, double scale)
        {
            EnsureLayerExists(tr, db, BaseCogLayer, 1); // Layer mới, Nét màu Đỏ (1)
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(BaseCogBlockName))
            {
                bt.UpgradeOpen();
                BlockTableRecord newBtr = new BlockTableRecord { Name = BaseCogBlockName };
                
                // Tròn gốc
                Circle c = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.3) { ColorIndex = 1 };
                // Trục X
                Line lx = new Line(Point3d.Origin, new Point3d(1.2, 0, 0)) { ColorIndex = 1 };
                Polyline px = new Polyline();
                px.AddVertexAt(0, new Point2d(1.0, 0.15), 0, 0, 0);
                px.AddVertexAt(1, new Point2d(1.2, 0), 0, 0, 0);
                px.AddVertexAt(2, new Point2d(1.0, -0.15), 0, 0, 0);
                px.ColorIndex = 1;
                // Trục Y
                Line ly = new Line(Point3d.Origin, new Point3d(0, 1.2, 0)) { ColorIndex = 1 };
                Polyline py = new Polyline();
                py.AddVertexAt(0, new Point2d(0.15, 1.0), 0, 0, 0);
                py.AddVertexAt(1, new Point2d(0, 1.2), 0, 0, 0);
                py.AddVertexAt(2, new Point2d(-0.15, 1.0), 0, 0, 0);
                py.ColorIndex = 1;

                newBtr.AppendEntity(c); newBtr.AppendEntity(lx); newBtr.AppendEntity(px);
                newBtr.AppendEntity(ly); newBtr.AppendEntity(py);
                
                bt.Add(newBtr); 
                tr.AddNewlyCreatedDBObject(newBtr, true);
            }
            BlockReference bref = new BlockReference(loc, bt[BaseCogBlockName]) { ScaleFactors = new Scale3d(scale), Layer = BaseCogLayer };
            btr.AppendEntity(bref); 
            tr.AddNewlyCreatedDBObject(bref, true);
        }

        // ĐÃ NÂNG CẤP: Lưu thêm tọa độ BasePoint vào XData
        public static void AddXData(Polyline pl, int no, string name, Point3d basePoint, Transaction tr, Database db)
        {
            RegAppTable rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(RegAppName)) 
            { 
                rat.UpgradeOpen(); 
                RegAppTableRecord regRecord = new RegAppTableRecord { Name = RegAppName };
                rat.Add(regRecord); 
                tr.AddNewlyCreatedDBObject(regRecord, true); 
            }
            
            pl.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, RegAppName),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, no),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, name),
                new TypedValue((int)DxfCode.ExtendedDataReal, basePoint.X), // Lưu X Gốc
                new TypedValue((int)DxfCode.ExtendedDataReal, basePoint.Y)  // Lưu Y Gốc
            ); 
        }

        public static bool HasOurXData(Entity ent)
        {
            if (ent == null || ent.XData == null) return false;
            return ent.XData.AsArray().Any(tv => tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value.ToString() == RegAppName);
        }
    }
}