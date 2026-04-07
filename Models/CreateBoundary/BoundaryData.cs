using Autodesk.AutoCAD.DatabaseServices;

namespace GetPropsTool.Models
{
    public class BoundaryData
    {
        public int No { get; set; }
        public string PlateName { get; set; }
        public double Area { get; set; }
        public double XCog { get; set; }
        public double YCog { get; set; }
        public ObjectId Id { get; set; }
    }
}