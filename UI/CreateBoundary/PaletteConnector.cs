using Autodesk.AutoCAD.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using GetPropsTool.Views;
using GetPropsTool.ViewModels;
using System;

namespace GetPropsTool.UI
{
    public class PaletteConnector
    {
        public static PaletteSet MyPS = null;
        public static BoundaryViewModel MyVM = null;

        public static void ShowPalette()
        {
            try
            {
                if (MyPS == null)
                {
                    // 1. Khởi tạo với định danh duy nhất
                    MyPS = new PaletteSet("MCG Boundary Manager", "SplitBoundary_Palette_V1", new Guid("D23B5A6F-7C4E-4B12-9D8A-C7F4E6A3B123"));
                    
                    // 2. Thiết lập Style NGAY LẬP TỨC (Chỉ làm 1 lần)
                    MyPS.Style = PaletteSetStyles.ShowCloseButton | 
                                 PaletteSetStyles.ShowAutoHideButton | 
                                 PaletteSetStyles.ShowPropertiesMenu;
                    
                    MyPS.Dock = DockSides.Right;
                    MyPS.MinimumSize = new System.Drawing.Size(300, 600);

                    // 3. Khởi tạo ViewModel và View
                    MyVM = new BoundaryViewModel();
                    var view = new BoundaryView { DataContext = MyVM };
                    MyPS.AddVisual("Plates", view);
                }

                // 4. Hiển thị Palette
                MyPS.Visible = true;
                
                // 5. Đồng bộ dữ liệu
                SyncData();
            }
            catch (Exception ex)
            {
                // Nếu lỗi do trạng thái (như AutoRollUp), ta bỏ qua để user vẫn dùng được bảng
                Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n[MCG] Palette Status: " + ex.Message);
            }
        }

        public static void SyncData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || MyVM == null) return;

            try 
            {
                // Sử dụng ScanDocument đã được fix lỗi IndexOutOfRange ở phiên bản trước
                var data = SplitBoundary.ScanDocument(doc.Database);
                MyVM.UpdateList(data);
            }
            catch { }
        }
    }
}