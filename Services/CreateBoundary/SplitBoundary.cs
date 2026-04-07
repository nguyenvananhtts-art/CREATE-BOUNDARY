using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.GraphicsInterface; 
using GetPropsTool.Models;
using GetPropsTool.ViewModels;

using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace GetPropsTool
{
    public class SplitBoundary
    {
        private static ObjectId _lastHighlightedId = ObjectId.Null;
        private static List<Entity> _transientGraphics = new List<Entity>();

        public static void ClearHighlightState() { _lastHighlightedId = ObjectId.Null; }

        private static void AddGhostHighlight(Entity ent) { if (ent == null) return; ent.ColorIndex = 3; TransientManager.CurrentTransientManager.AddTransient(ent, TransientDrawingMode.Main, 128, new IntegerCollection()); _transientGraphics.Add(ent); }
        private static void ClearGhostHighlights() { if (_transientGraphics.Count == 0) return; foreach (var ent in _transientGraphics) { try { TransientManager.CurrentTransientManager.EraseTransient(ent, new IntegerCollection()); } catch { } if (!ent.IsDisposed) ent.Dispose(); } _transientGraphics.Clear(); }

        private static Extents3d GetSelectionExtents(ObjectId[] ids, Transaction tr)
        {
            Extents3d ext = new Extents3d();
            bool first = true;
            foreach(var id in ids) {
                try {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.Bounds.HasValue) {
                        if (first) { ext = ent.Bounds.Value; first = false; }
                        else { ext.AddExtents(ent.Bounds.Value); }
                    }
                } catch {}
            }
            return ext;
        }

        public static void ExecuteCreate(Editor ed, BoundaryViewModel vm)
        {
            if (vm == null) return;
            ed.WriteMessage("\n[MCG] --- TIẾN TRÌNH TẠO PLATE (ROUTING MODE) ---");
            if (vm.IsMethodSplitLines) RunMethodSplitLines(ed, vm);
            else RunMethodPickPoint(ed, vm);
        }

        // ==============================================================================
        // OPTION 1: ÁP DỤNG THÁC NƯỚC 3 TẦNG
        // ==============================================================================
        private static void RunMethodSplitLines(Editor ed, BoundaryViewModel vm)
        {
            Document doc = ed.Document;
            Database db = doc.Database;
            PromptSelectionOptions pso = new PromptSelectionOptions { MessageForAdding = "\n[Option 1] Quét chọn khung bao và các đường chia:" };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            PromptPointOptions ppoBase = new PromptPointOptions("\n[MCG] Pick điểm gốc tọa độ CNC (Nhấn Enter để dùng WCS mặc định 0,0,0):");
            ppoBase.AllowNone = true;
            PromptPointResult pprBase = ed.GetPoint(ppoBase);
            
            Point3d basePt = Point3d.Origin;
            bool hasBasePt = (pprBase.Status == PromptStatus.OK);
            if (hasBasePt) basePt = pprBase.Value;

            List<Curve> allCurves = new List<Curve>();
            double globalScale = 1.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Extents3d ext = GetSelectionExtents(psr.Value.GetObjectIds(), tr);
                try { double diag = ext.MaxPoint.DistanceTo(ext.MinPoint); if (diag > 0) globalScale = diag * 0.05; } catch {}

                foreach (SelectedObject so in psr.Value)
                {
                    var cur = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                    if (cur != null) 
                    {
                        Curve flat = cur.Clone() as Curve;
                        flat.TransformBy(Matrix3d.WorldToPlane(new Plane(Point3d.Origin, Vector3d.ZAxis)));
                        if (flat is Polyline || flat is Polyline2d || flat is Polyline3d)
                        {
                            DBObjectCollection ex = new DBObjectCollection();
                            flat.Explode(ex);
                            foreach (DBObject obj in ex) { if (obj is Curve exCurve) allCurves.Add(exCurve); }
                            flat.Dispose();
                        }
                        else allCurves.Add(flat);
                    }
                }
                tr.Commit();
            }

            bool requiresExtension = CheckIfRequiresExtension(allCurves);
            List<Region> finalRegions = new List<Region>();

            // THÁC NƯỚC TẦNG 1 & TẦNG 2
            if (requiresExtension) finalRegions = Case2_ExtensionSplitLine.GetRegions(allCurves, ed);
            else {
                finalRegions = Case1_BasicSplit.GetRegions(allCurves, ed);
                if (finalRegions.Count == 0) finalRegions = Case2_ExtensionSplitLine.GetRegions(allCurves, ed);
            }

            // THÁC NƯỚC TẦNG 3: Chỉ kích hoạt khi Tầng 1 và Tầng 2 bó tay (Ví dụ: 2 line song song)
            if (finalRegions.Count == 0)
            {
                ed.WriteMessage("\n[MCG-CỨU HỘ] Tầng 2 thất bại do không tìm thấy giao điểm. Kích hoạt Tầng 3 (Bắc Cầu)...");
                finalRegions = Case3_EndpointBridging.GetRegions(allCurves, ed);
            }

            if (finalRegions.Count > 0)
            {
                using (DocumentLock loc = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    
                    if (hasBasePt) CreateBoundary.InsertBaseCogBlock(tr, db, btr, basePt, globalScale);

                    int plateCount = 0;
                    int startNo = GetLastNumber(db) + 1;

                    foreach (Region reg in finalRegions)
                    {
                        BoundaryUtils.ProcessRegionToPlate(reg, tr, btr, db, $"PL-{startNo + plateCount}", startNo + plateCount, basePt, vm, ed);
                        plateCount++;
                        reg.Dispose();
                    }

                    if (vm.IsDeleteOriginal) 
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            try { Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity; if (ent != null) ent.Erase(); } catch { }
                        }
                    }
                    tr.Commit();
                    ed.WriteMessage($"\n[MCG] THÀNH CÔNG (Option 1): Đã tạo {plateCount} tấm thép.");
                }
            }
            else
            {
                ed.WriteMessage("\n[MCG] THẤT BẠI: Hình học không hợp lệ. Đã thử cả 3 thuật toán nhưng không thành công.");
            }
            ed.UpdateScreen();
            UI.PaletteConnector.SyncData();
        }

        private static bool CheckIfRequiresExtension(List<Curve> curves) { foreach (Curve c in curves) { if (c is Line line) { bool startTouches = false; bool endTouches = false; foreach (Curve target in curves) { if (target == line) continue; if (target.GetClosestPointTo(line.StartPoint, false).DistanceTo(line.StartPoint) < 1e-4) startTouches = true; if (target.GetClosestPointTo(line.EndPoint, false).DistanceTo(line.EndPoint) < 1e-4) endTouches = true; } if (!startTouches || !endTouches) return true; } } return false; }

        public static List<BoundaryData> ScanDocument(Database db)
        {
            var res = new List<BoundaryData>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btrId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsNull || id.IsErased || !id.IsValid) continue;
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is Polyline pl && CreateBoundary.HasOurXData(pl))
                    {
                        var tvs = pl.XData.AsArray();
                        if (tvs.Length >= 3) 
                        {
                            double baseX = 0.0, baseY = 0.0;
                            if (tvs.Length >= 5) { baseX = Convert.ToDouble(tvs[3].Value); baseY = Convert.ToDouble(tvs[4].Value); }

                            Point3d c = BoundaryUtils.GetCentroidFromPolyline(pl);
                            res.Add(new BoundaryData { 
                                No = (int)tvs[1].Value, 
                                PlateName = tvs[2].Value.ToString(), 
                                Area = pl.Area, 
                                XCog = Math.Round(c.X - baseX, 2), 
                                YCog = Math.Round(c.Y - baseY, 2), 
                                Id = id 
                            });
                        }
                    }
                }
                tr.Commit();
            }
            return res;
        }

        public static int GetLastNumber(Database db) { var d = ScanDocument(db); return d.Count == 0 ? 0 : d.Max(p => p.No); }

        public static void HighlightPlateSafe(ObjectId id) { Document doc = Application.DocumentManager.MdiActiveDocument; if (doc == null) return; Editor ed = doc.Editor; try { if (id.IsNull || id.IsErased || !id.IsValid) return; if (id.Database != doc.Database) return; using (DocumentLock loc = doc.LockDocument()) { using (var tr = doc.Database.TransactionManager.StartTransaction()) { if (_lastHighlightedId != ObjectId.Null && !_lastHighlightedId.IsErased && _lastHighlightedId.IsValid) { if (_lastHighlightedId.Database == doc.Database) { Entity oldEnt = tr.GetObject(_lastHighlightedId, OpenMode.ForRead) as Entity; if (oldEnt != null) oldEnt.Unhighlight(); } } Entity newEnt = tr.GetObject(id, OpenMode.ForRead) as Entity; if (newEnt != null) { newEnt.Highlight(); _lastHighlightedId = id; } tr.Commit(); } } ed.UpdateScreen(); } catch { } }

        // ==============================================================================
        // OPTION 2: PICK POINT (ÁP DỤNG THÁC NƯỚC 3 TẦNG)
        // ==============================================================================
        private static void RunMethodPickPoint(Editor ed, BoundaryViewModel vm) 
        { 
            Document doc = ed.Document;
            Database db = doc.Database;
            int plateCount = 0;
            int startNo = GetLastNumber(db) + 1;

            ed.WriteMessage("\n[MCG] KÍCH HOẠT OPTION 2: Pick Point (Sức mạnh Toán học Lõi).");

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\n[Option 2] Quét chọn khung bao và các đường chia (Giữ Shift để bỏ chọn). Nhấn Enter để tiếp tục:";
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            PromptPointOptions ppoBase = new PromptPointOptions("\n[MCG] Pick điểm gốc tọa độ CNC (Nhấn Enter để dùng WCS mặc định 0,0,0):");
            ppoBase.AllowNone = true;
            PromptPointResult pprBase = ed.GetPoint(ppoBase);
            
            Point3d basePt = Point3d.Origin;
            bool hasBasePt = (pprBase.Status == PromptStatus.OK);
            if (hasBasePt) basePt = pprBase.Value;

            HashSet<ObjectId> selectedIds = new HashSet<ObjectId>(psr.Value.GetObjectIds());
            List<ObjectId> hiddenIds = new List<ObjectId>(); 
            List<Curve> allCurves = new List<Curve>();
            double globalScale = 1.0;

            using (DocumentLock loc = doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Extents3d ext = GetSelectionExtents(psr.Value.GetObjectIds(), tr);
                    try { double diag = ext.MaxPoint.DistanceTo(ext.MinPoint); if (diag > 0) globalScale = diag * 0.05; } catch {}

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    if (hasBasePt) CreateBoundary.InsertBaseCogBlock(tr, db, btr, basePt, globalScale);

                    foreach (ObjectId id in btr)
                    {
                        if (!selectedIds.Contains(id))
                        {
                            try { Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity; if (ent != null && ent.Visible) { ent.Visible = false; hiddenIds.Add(id); } } catch { }
                        }
                        else
                        {
                            var cur = tr.GetObject(id, OpenMode.ForRead) as Curve;
                            if (cur != null) 
                            {
                                Curve flat = cur.Clone() as Curve;
                                flat.TransformBy(Matrix3d.WorldToPlane(new Plane(Point3d.Origin, Vector3d.ZAxis)));
                                if (flat is Polyline || flat is Polyline2d || flat is Polyline3d)
                                {
                                    DBObjectCollection ex = new DBObjectCollection();
                                    flat.Explode(ex);
                                    foreach (DBObject obj in ex) { if (obj is Curve exCurve) allCurves.Add(exCurve); }
                                    flat.Dispose();
                                }
                                else allCurves.Add(flat);
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            ed.Regen(); 
            ClearGhostHighlights();

            ed.WriteMessage("\n[MCG] Đang tính toán các miền kín (Pre-Calculation)...");
            bool requiresExt = CheckIfRequiresExtension(allCurves);
            List<Region> availableRegions = new List<Region>();

            // THÁC NƯỚC TẦNG 1 & TẦNG 2 CHO OPTION 2
            if (requiresExt) availableRegions = Case2_ExtensionSplitLine.GetRegions(allCurves, ed);
            else { availableRegions = Case1_BasicSplit.GetRegions(allCurves, ed); if (availableRegions.Count == 0) availableRegions = Case2_ExtensionSplitLine.GetRegions(allCurves, ed); }

            // THÁC NƯỚC TẦNG 3 CHO OPTION 2
            if (availableRegions.Count == 0)
            {
                ed.WriteMessage("\n[MCG-CỨU HỘ] Kích hoạt Tầng 3 (Bắc Cầu)...");
                availableRegions = Case3_EndpointBridging.GetRegions(allCurves, ed);
            }

            ed.WriteMessage($"\n[MCG] Hoàn tất tính toán! Đã lưu sẵn {availableRegions.Count} miền vào bộ nhớ.");

            while (true)
            {
                PromptPointOptions ppo = new PromptPointOptions("\nClick điểm vào bên trong miền kín (Nhấn Enter hoặc Esc để kết thúc):");
                ppo.AllowNone = true; 
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) break;

                Point3d pt = ppr.Value;
                Region clickedRegion = null;

                foreach (Region r in availableRegions) { if (BoundaryUtils.IsPointInRegion(r, pt)) { clickedRegion = r; break; } }

                if (clickedRegion != null)
                {
                    Polyline ghostClone = null; 
                    using (DocumentLock loc = doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        
                        string pName = $"PL-{startNo + plateCount}";
                        Polyline newPl = BoundaryUtils.ProcessRegionToPlate(clickedRegion, tr, btr, db, pName, startNo + plateCount, basePt, vm, ed);
                        
                        if (newPl != null) ghostClone = (Polyline)newPl.Clone();
                        plateCount++;
                        ed.WriteMessage($"\n  -> Đã tạo thành công: {pName}");
                        tr.Commit();
                    } 
                    if (ghostClone != null) { AddGhostHighlight(ghostClone); ed.UpdateScreen(); }
                    availableRegions.Remove(clickedRegion); clickedRegion.Dispose();
                }
                else ed.WriteMessage("\n[Cảnh báo] Điểm click không nằm trong miền kín nào, hoặc miền này đã được tạo.");
            }

            ClearGhostHighlights();
            foreach (Region r in availableRegions) r.Dispose();

            using (DocumentLock loc = doc.LockDocument())
            {
                if (hiddenIds.Count > 0)
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in hiddenIds) { try { Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity; if (ent != null) ent.Visible = true; } catch { } }
                        tr.Commit();
                    }
                }

                if (vm.IsDeleteOriginal && plateCount > 0)
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in selectedIds) { try { Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity; if (ent != null) ent.Erase(); } catch { } }
                        tr.Commit();
                    }
                }
            } 
            ed.Regen(); 
            if (plateCount > 0)
            {
                ed.WriteMessage($"\n[MCG] THÀNH CÔNG (Option 2): Đã tạo tổng cộng {plateCount} tấm thép.");
                UI.PaletteConnector.SyncData();
            }
        }
    }
}