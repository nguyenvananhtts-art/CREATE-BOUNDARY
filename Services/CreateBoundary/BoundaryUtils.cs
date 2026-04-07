using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using GetPropsTool.ViewModels;

namespace GetPropsTool
{
    public static class BoundaryUtils
    {
        // ... (Giữ nguyên các hàm PurifyInputCurves, PurifyPolyline, ShatterCurves, CleanNetwork, FilterInnerMost) ...
        
        public static List<Curve> PurifyInputCurves(List<Curve> curves) { /* ... Giữ nguyên ... */ return curves.Where(c => { try { return c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam) > 1e-3; } catch { return false; } }).ToList(); }
        public static Polyline PurifyPolyline(Polyline pl) { /* ... Giữ nguyên ... */ if (pl == null || pl.NumberOfVertices < 2) return null; Polyline cleanPl = new Polyline(); int idx = 0; double tol = 1e-3; Point2d lastPt = pl.GetPoint2dAt(0); cleanPl.AddVertexAt(idx++, lastPt, pl.GetBulgeAt(0), 0, 0); for (int i = 1; i < pl.NumberOfVertices; i++) { Point2d pt = pl.GetPoint2dAt(i); if (pt.GetDistanceTo(lastPt) > tol) { cleanPl.AddVertexAt(idx++, pt, pl.GetBulgeAt(i), 0, 0); lastPt = pt; } } if (cleanPl.Closed && cleanPl.NumberOfVertices > 2) { if (cleanPl.GetPoint2dAt(cleanPl.NumberOfVertices - 1).GetDistanceTo(cleanPl.GetPoint2dAt(0)) < tol) cleanPl.RemoveVertexAt(cleanPl.NumberOfVertices - 1); } cleanPl.Closed = pl.Closed; return cleanPl; }
        public static DBObjectCollection ShatterCurves(DBObjectCollection curves) { /* ... Giữ nguyên ... */ DBObjectCollection result = new DBObjectCollection(); foreach (Curve c1 in curves) { Point3dCollection pts = new Point3dCollection(); foreach (Curve c2 in curves) { if (c1 != c2) c1.IntersectWith(c2, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero); } List<double> pList = new List<double>(); foreach (Point3d p in pts) { try { pList.Add(c1.GetParameterAtPoint(p)); } catch { } } var distinctParams = pList.OrderBy(x => x).ToList(); DoubleCollection dParams = new DoubleCollection(); double lastP = -1.0; foreach (double p in distinctParams) { if (Math.Abs(p - lastP) > 1e-5 && p > c1.StartParam + 1e-5 && p < c1.EndParam - 1e-5) { dParams.Add(p); lastP = p; } } if (dParams.Count > 0) { try { var pieces = c1.GetSplitCurves(dParams); foreach (DBObject p in pieces) { if (p is Curve pc && pc.GetDistanceAtParameter(pc.EndParam) > 1e-4) result.Add(p); else p.Dispose(); } } catch { if (c1.GetDistanceAtParameter(c1.EndParam) > 1e-4) result.Add(c1); } } else { if (c1.GetDistanceAtParameter(c1.EndParam) > 1e-4) result.Add(c1); } } return result; }
        public static DBObjectCollection CleanNetwork(DBObjectCollection shatteredCurves) { /* ... Giữ nguyên ... */ List<Curve> curves = shatteredCurves.Cast<Curve>().ToList(); bool removedAny = true; double tol = 1e-4; while (removedAny) { removedAny = false; for (int i = curves.Count - 1; i >= 0; i--) { Curve c = curves[i]; bool startConnected = false; bool endConnected = false; if (c.Closed || c.StartPoint.DistanceTo(c.EndPoint) < tol) continue; for (int j = 0; j < curves.Count; j++) { if (i == j) continue; Curve other = curves[j]; if (!startConnected && (c.StartPoint.DistanceTo(other.StartPoint) < tol || c.StartPoint.DistanceTo(other.EndPoint) < tol)) startConnected = true; if (!endConnected && (c.EndPoint.DistanceTo(other.StartPoint) < tol || c.EndPoint.DistanceTo(other.EndPoint) < tol)) endConnected = true; if (startConnected && endConnected) break; } if (!startConnected || !endConnected) { curves.RemoveAt(i); removedAny = true; } } } DBObjectCollection cleanResult = new DBObjectCollection(); foreach (var c in curves) cleanResult.Add(c); return cleanResult; }
        public static List<Region> FilterInnerMost(List<Region> sortedRegs) { /* ... Giữ nguyên ... */ List<Region> result = new List<Region>(); for (int i = 0; i < sortedRegs.Count; i++) { bool isParent = false; for (int j = 0; j < i; j++) { if (IsPointInRegion(sortedRegs[i], GetCentroidFromRegion(sortedRegs[j]))) { isParent = true; break; } } if (!isParent) result.Add(sortedRegs[i]); } return result; }

        // ĐÃ CẬP NHẬT: Nhận thêm biến Point3d basePoint
        public static Polyline ProcessRegionToPlate(Region reg, Transaction tr, BlockTableRecord btr, Database db, string name, int no, Point3d basePoint, BoundaryViewModel vm, Editor ed)
        {
            Point3d cog = GetCentroidFromRegion(reg);
            Polyline pl = ConvertRegionToPolyline(reg, ed, name);
            if (pl != null) 
            {
                CreateBoundary.EnsureLayerExists(tr, db, CreateBoundary.TargetLayer, 4);
                pl.SetDatabaseDefaults(); pl.Layer = CreateBoundary.TargetLayer; pl.ColorIndex = 256;
                btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                
                // Truyền basePoint vào XData
                CreateBoundary.AddXData(pl, no, name, basePoint, tr, db);
                
                double baseScale = reg.GeometricExtents.MinPoint.DistanceTo(reg.GeometricExtents.MaxPoint) * 0.08;
                double cogScale = baseScale * 0.5;
                double yOffset = (0.5 * cogScale) + (baseScale * 0.5) + (baseScale * 0.2);
                Point3d textLoc = new Point3d(cog.X, cog.Y + yOffset, cog.Z);

                if (vm.IsCreateText) CreateBoundary.CreateMText(tr, btr, textLoc, name, baseScale);
                if (vm.IsInsertCog) CreateBoundary.InsertCogBlock(tr, db, btr, cog, cogScale);
            }
            return pl;
        }

        public static Polyline ConvertRegionToPolyline(Region reg, Editor ed, string name) { /* ... Giữ nguyên ... */ DBObjectCollection ex = new DBObjectCollection(); reg.Explode(ex); List<Curve> segs = ex.Cast<Curve>().ToList(); if (segs.Count == 0) return null; Polyline pl = new Polyline(); Plane plane = new Plane(Point3d.Origin, Vector3d.ZAxis); Curve first = segs[0]; Point3d next = first.EndPoint; pl.AddVertexAt(0, first.StartPoint.Convert2d(plane), GetBulge(first, false), 0, 0); pl.AddVertexAt(1, next.Convert2d(plane), 0, 0, 0); segs.RemoveAt(0); int vIdx = 2; while (segs.Count > 0) { bool found = false; for (int i = 0; i < segs.Count; i++) { if (segs[i].StartPoint.DistanceTo(next) < 1e-3) { pl.SetBulgeAt(vIdx - 1, GetBulge(segs[i], false)); next = segs[i].EndPoint; pl.AddVertexAt(vIdx++, next.Convert2d(plane), 0, 0, 0); segs.RemoveAt(i); found = true; break; } else if (segs[i].EndPoint.DistanceTo(next) < 1e-3) { pl.SetBulgeAt(vIdx - 1, GetBulge(segs[i], true)); next = segs[i].StartPoint; pl.AddVertexAt(vIdx++, next.Convert2d(plane), 0, 0, 0); segs.RemoveAt(i); found = true; break; } } if (!found) break; } pl.Closed = true; return PurifyPolyline(pl); }
        public static double GetBulge(Curve cur, bool inv) { /* ... Giữ nguyên ... */ if (cur is Arc arc) { double d = arc.EndAngle - arc.StartAngle; if (d < 0) d += 2 * Math.PI; return Math.Tan(d / 4.0) * (inv ? -1.0 : 1.0); } return 0.0; }
        public static Point3d GetCentroidFromRegion(Region reg) { /* ... Giữ nguyên ... */ Point3d o = Point3d.Origin; Vector3d x = Vector3d.XAxis; Vector3d y = Vector3d.YAxis; var prop = reg.AreaProperties(ref o, ref x, ref y); Point3d localCentroid = new Point3d(prop.Centroid.X, prop.Centroid.Y, 0.0); Plane plane = new Plane(Point3d.Origin, reg.Normal); return localCentroid.TransformBy(Matrix3d.PlaneToWorld(plane)); }
        public static Point3d GetCentroidFromPolyline(Polyline pl) { /* ... Giữ nguyên ... */ try { DBObjectCollection col = new DBObjectCollection(); col.Add(pl); using (var rs = Region.CreateFromCurves(col)) { if (rs != null && rs.Count > 0) return GetCentroidFromRegion((Region)rs[0]); } } catch { } return Point3d.Origin; }
        public static bool IsPointInRegion(Region reg, Point3d pt) { /* ... Giữ nguyên ... */ Extents3d ext = reg.GeometricExtents; if (pt.X < ext.MinPoint.X - 1e-3 || pt.X > ext.MaxPoint.X + 1e-3 || pt.Y < ext.MinPoint.Y - 1e-3 || pt.Y > ext.MaxPoint.Y + 1e-3) return false; try { using (Circle cir = new Circle(pt, Vector3d.ZAxis, 0.1)) { DBObjectCollection col = new DBObjectCollection(); col.Add(cir); using (DBObjectCollection tinyRegs = Region.CreateFromCurves(col)) { if (tinyRegs != null && tinyRegs.Count > 0) { using (Region tinyReg = (Region)tinyRegs[0]) using (Region cloneReg = (Region)reg.Clone()) { cloneReg.BooleanOperation(BooleanOperationType.BoolIntersect, tinyReg); return cloneReg.Area > 0; } } } } } catch { } return false; }
    }
}