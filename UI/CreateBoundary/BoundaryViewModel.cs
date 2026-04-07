using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using GetPropsTool.Models;

namespace GetPropsTool.ViewModels
{
    public class BoundaryViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<BoundaryData> Boundaries { get; set; } = new ObservableCollection<BoundaryData>();

        private string _prefix = "PL-";
        public string Prefix 
        { 
            get => _prefix; 
            set { _prefix = value; OnPropertyChanged(); } 
        }

        private bool _isMethodSplitLines = true;
        public bool IsMethodSplitLines 
        { 
            get => _isMethodSplitLines; 
            set { _isMethodSplitLines = value; OnPropertyChanged(); } 
        }

        private bool _isMethodPickPoint = false;
        public bool IsMethodPickPoint 
        { 
            get => _isMethodPickPoint; 
            set { _isMethodPickPoint = value; OnPropertyChanged(); } 
        }

        public bool IsCreateText { get; set; } = true;
        public bool IsDeleteOriginal { get; set; } = false;
        public bool IsInsertCog { get; set; } = true;

        public string TotalSummary => $"Area: {Boundaries.Sum(x => x.Area):N2} | Count: {Boundaries.Count}";

        public void UpdateList(System.Collections.Generic.List<BoundaryData> newData)
        {
            Boundaries.Clear();
            foreach (var item in newData) Boundaries.Add(item);
            OnPropertyChanged(nameof(TotalSummary));
        }

        // ============================================================
        // PHẦN CÒN THIẾU GÂY LỖI CS0535: IMPLEMENT INTERFACE
        // ============================================================
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}