using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace GetPropsTool
{
    public class Case1_BasicSplit
    {
        public static List<Region> GetRegions(List<Curve> curves, Editor ed)
        {
            List<Region> result = new List<Region>();
            List<Curve> cleanCurves = BoundaryUtils.PurifyInputCurves(curves);
            
            DBObjectCollection curveCol = new DBObjectCollection();
            foreach (var c in cleanCurves) curveCol.Add(c);

            DBObjectCollection shattered = BoundaryUtils.ShatterCurves(curveCol);
            
            try
            {
                using (DBObjectCollection regions = Region.CreateFromCurves(shattered))
                {
                    if (regions == null || regions.Count == 0) return result;

                    var validRegs = regions.Cast<Region>().Where(r => r.Area > 1.0).OrderBy(r => r.Area).ToList();
                    List<Region> innerRegs = BoundaryUtils.FilterInnerMost(validRegs);

                    foreach (Region reg in innerRegs)
                    {
                        result.Add((Region)reg.Clone()); // Trả về bản sao để thoát khỏi using an toàn
                    }
                }
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\n[MCG] LỖI TẠO MIỀN (Case 1): {ex.Message}");
            }
            return result;
        }
    }
}