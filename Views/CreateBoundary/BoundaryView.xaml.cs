using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GetPropsTool.Models;
using GetPropsTool.UI;

namespace GetPropsTool.Views
{
    public partial class BoundaryView : UserControl
    {
        public BoundaryView()
        {
            InitializeComponent();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e) 
        { 
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.SendStringToExecute("MCG_INTERNAL_CREATE ", true, false, false);
            }
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            if (PaletteConnector.MyVM != null)
            {
                // Xóa danh sách hiển thị trên WPF
                PaletteConnector.MyVM.Boundaries.Clear();
                PaletteConnector.MyVM.UpdateList(new System.Collections.Generic.List<BoundaryData>());
                
                // VACCINE 3: Tẩy trắng trí nhớ biến Static bên lõi CAD
                SplitBoundary.ClearHighlightState();
                
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\n[MCG] Đã dọn dẹp dữ liệu và reset trạng thái Highlight.");
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\n[WPF] Đã bắt được sự kiện Click trên DataGrid.");

                if (e.AddedItems != null && e.AddedItems.Count > 0)
                {
                    if (e.AddedItems[0] is BoundaryData data)
                    {
                        if (doc != null) doc.Editor.WriteMessage($"\n[WPF] Yêu cầu làm sáng Plate: {data.PlateName}");
                        SplitBoundary.HighlightPlateSafe(data.Id);
                    }
                    else
                    {
                        if (doc != null) doc.Editor.WriteMessage("\n[WPF] LỖI: Dữ liệu dòng không phải là BoundaryData.");
                    }
                }
            }
            catch (Exception ex)
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage($"\n[WPF] LỖI FATAL NGẦM: {ex.Message}");
            }
        }

        private void MenuItem_CopyData_Click(object sender, RoutedEventArgs e)
        {
            var vm = PaletteConnector.MyVM;
            if (vm == null || vm.Boundaries.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("No\tPlateName\tArea\tX_COG\tY_COG");
            foreach (var item in vm.Boundaries)
            {
                sb.AppendLine($"{item.No}\t{item.PlateName}\t{item.Area:F2}\t{item.XCog:F2}\t{item.YCog:F2}");
            }
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Data copied to Clipboard!", "MCG Tool");
        }
    }
}