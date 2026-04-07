using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace GetPropsTool
{
    public class Case3_EndpointBridging
    {
        // Class phụ trợ để quản lý các điểm đầu mút
        private class PointData
        {
            public Point3d Point { get; set; }
            public Curve ParentCurve { get; set; }
            public bool IsPaired { get; set; } = false;
        }

        public static List<Region> GetRegions(List<Curve> curves, Editor ed)
        {
            List<Region> result = new List<Region>();
            double maxBridgeDist = 5000.0; // GIỚI HẠN BẮC CẦU: 5000 mm
            double tol = 1e-3;

            ed.WriteMessage($"\n[MCG] === TẦNG 3: Thuật toán Bắc Cầu (Max Dist: {maxBridgeDist}mm) ===");

            // 1. Thu thập toàn bộ StartPoint và EndPoint
            List<PointData> allPoints = new List<PointData>();
            foreach (Curve c in curves)
            {
                allPoints.Add(new PointData { Point = c.StartPoint, ParentCurve = c });
                // Tránh add trùng nếu đường đó là hình khép kín (Đầu trùng Đuôi)
                if (!c.Closed && c.StartPoint.DistanceTo(c.EndPoint) > tol)
                {
                    allPoints.Add(new PointData { Point = c.EndPoint, ParentCurve = c });
                }
            }

            // 2. Tìm "Điểm mồ côi" (Những điểm không chạm vào bất kỳ điểm nào khác)
            List<PointData> orphans = new List<PointData>();
            for (int i = 0; i < allPoints.Count; i++)
            {
                bool isOrphan = true;
                for (int j = 0; j < allPoints.Count; j++)
                {
                    if (i == j) continue;
                    if (allPoints[i].Point.DistanceTo(allPoints[j].Point) < tol)
                    {
                        isOrphan = false;
                        break;
                    }
                }
                if (isOrphan) orphans.Add(allPoints[i]);
            }

            if (orphans.Count < 2)
            {
                ed.WriteMessage("\n[MCG] Không đủ điểm đứt gãy để tiến hành nối.");
                return result;
            }

            List<Curve> bridges = new List<Curve>();

            // 3. Mai mối: Nối các điểm mồ côi gần nhau nhất (Nearest Neighbor)
            foreach (var o1 in orphans)
            {
                if (o1.IsPaired) continue;

                PointData bestMatch = null;
                double minDist = double.MaxValue;

                foreach (var o2 in orphans)
                {
                    if (o2 == o1 || o2.IsPaired) continue;
                    
                    // CỰC KỲ QUAN TRỌNG: Cấm tự nối 2 đầu của cùng 1 đường thẳng
                    if (o1.ParentCurve == o2.ParentCurve) continue; 

                    double dist = o1.Point.DistanceTo(o2.Point);
                    if (dist < minDist && dist <= maxBridgeDist)
                    {
                        minDist = dist;
                        bestMatch = o2;
                    }
                }

                if (bestMatch != null)
                {
                    bridges.Add(new Line(o1.Point, bestMatch.Point));
                    o1.IsPaired = true;
                    bestMatch.IsPaired = true;
                    ed.WriteMessage($"\n[MCG] Đã bắc cầu nối 1 khoảng hở dài: {Math.Round(minDist, 2)} mm.");
                }
            }

            if (bridges.Count == 0)
            {
                ed.WriteMessage($"\n[MCG] Các điểm đứt gãy đều xa hơn {maxBridgeDist}mm. Từ chối nối.");
                return result;
            }

            // 4. Hợp nhất đường Bắc Cầu với các đường gốc và trả về Tầng 1 để vẽ
            List<Curve> combinedCurves = new List<Curve>(curves);
            combinedCurves.AddRange(bridges);

            ed.WriteMessage("\n[MCG] Tiến hành tạo miền với các đường bắc cầu mới...");
            result = Case1_BasicSplit.GetRegions(combinedCurves, ed);

            // Bọc lót siêu cấp: Lỡ bắc cầu xong mà hình vẫn chưa chịu kín, đẩy xuống Case 2 bắn tia tiếp
            if (result.Count == 0)
            {
                result = Case2_ExtensionSplitLine.GetRegions(combinedCurves, ed);
            }

            return result;
        }
    }
}