using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using MissionPlanner.Utilities.AviationLayers;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Side panel listing NOTAM/TFR and restriction counts for the current map view.
    /// </summary>
    public class MapNotamBriefingPanel : UserControl
    {
        static readonly Color PanelBack = Color.FromArgb(38, 38, 42);
        static readonly Color HeaderBack = Color.FromArgb(28, 28, 32);
        static readonly Color TextPrimary = Color.FromArgb(245, 245, 245);
        static readonly Color TextSecondary = Color.FromArgb(180, 180, 190);
        static readonly Color ListBack = Color.FromArgb(48, 48, 54);
        static readonly Color ListAlt = Color.FromArgb(42, 42, 48);
        static readonly Color GridLine = Color.FromArgb(70, 70, 78);

        private readonly Panel _headerBar;
        private readonly Label _lblHeader;
        private readonly Label _lblSummary;
        private readonly SplitContainer _split;
        private readonly ListView _list;
        private readonly TextBox _detail;
        private readonly Button _btnZoom;
        private readonly Button _btnOpen;
        private List<NotamBriefingItem> _items = new List<NotamBriefingItem>();

        public event Action<NotamBriefingItem> ZoomToItem;
        public event Action<NotamBriefingItem> OpenDetail;
        public event Action CloseRequested;

        public MapNotamBriefingPanel()
        {
            BackColor = PanelBack;
            MinimumSize = new Size(280, 200);

            _headerBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = HeaderBack,
                Padding = new Padding(0)
            };

            _lblHeader = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Restriction briefing",
                Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                BackColor = HeaderBack,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            var btnClose = new Button
            {
                Dock = DockStyle.Right,
                Width = 32,
                Text = "×",
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextPrimary,
                BackColor = HeaderBack,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                TabStop = false,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(90, 40, 40);
            btnClose.Click += (s, e) => CloseRequested?.Invoke();

            _headerBar.Controls.Add(_lblHeader);
            _headerBar.Controls.Add(btnClose);

            _lblSummary = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                AutoSize = false,
                Text = "Click a LAANC grid cell on the map or pick a row below for details.",
                ForeColor = TextSecondary,
                BackColor = PanelBack,
                Padding = new Padding(8, 6, 8, 4)
            };

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = PanelBack,
                Padding = new Padding(6, 4, 6, 6)
            };

            _btnZoom = CreateActionButton("Zoom to");
            _btnZoom.Click += (s, e) => InvokeSelected(ZoomToItem);

            _btnOpen = CreateActionButton("FAA detail");
            _btnOpen.Click += (s, e) =>
            {
                var item = SelectedItem();
                if (item == null)
                    return;
                OpenDetail?.Invoke(item);
                if (!string.IsNullOrEmpty(item.DetailUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(item.DetailUrl) { UseShellExecute = true });
                    }
                    catch
                    {
                    }
                }
            };

            buttonRow.Controls.Add(_btnZoom);
            buttonRow.Controls.Add(_btnOpen);

            _detail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(34, 34, 38),
                ForeColor = TextPrimary,
                Font = new Font(Font.FontFamily, 9f),
                Text = "Select a restriction to see details here."
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                BackColor = ListBack,
                ForeColor = TextPrimary,
                Font = new Font(Font.FontFamily, 8.75f)
            };
            _list.Columns.Add("ID", 72);
            _list.Columns.Add("Type", 64);
            _list.Columns.Add("Description", 180);
            _list.OwnerDraw = true;
            _list.DrawColumnHeader += List_DrawColumnHeader;
            _list.DrawItem += List_DrawItem;
            _list.DrawSubItem += List_DrawSubItem;
            _list.SelectedIndexChanged += (s, e) =>
            {
                UpdateButtons();
                ShowDetailForSelected();
            };
            _list.DoubleClick += (s, e) => InvokeSelected(ZoomToItem);

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = PanelBack,
                SplitterWidth = 6,
                Panel1MinSize = 72,
                Panel2MinSize = 80
            };
            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(_detail);
            _split.Panel1.BackColor = PanelBack;
            _split.Panel2.BackColor = PanelBack;

            Load += (s, e) =>
            {
                var half = Math.Max(_split.Panel1MinSize,
                    Math.Min(Height - _split.Panel2MinSize - 120, (int)(Height * 0.42)));
                if (half > _split.Panel1MinSize && half < Height - _split.Panel2MinSize - 80)
                    _split.SplitterDistance = half;
            };

            Controls.Add(_split);
            Controls.Add(buttonRow);
            Controls.Add(_lblSummary);
            Controls.Add(_headerBar);
        }

        public void SelectItem(NotamBriefingItem item)
        {
            if (item == null)
                return;

            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag is NotamBriefingItem tagged &&
                    string.Equals(tagged.NotamKey, item.NotamKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(tagged.Type, item.Type, StringComparison.OrdinalIgnoreCase))
                {
                    row.Selected = true;
                    row.Focused = true;
                    row.EnsureVisible();
                    ShowDetailForItem(item);
                    return;
                }
            }

            ShowDetailForItem(item);
        }

        static Button CreateActionButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextPrimary,
                BackColor = Color.FromArgb(58, 58, 66),
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(8, 2, 8, 2),
                Enabled = false
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 100);
            return btn;
        }

        void List_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var brush = new SolidBrush(HeaderBack))
                e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, e.Bounds, TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        void List_DrawItem(object sender, DrawListViewItemEventArgs e) => e.DrawDefault = false;

        void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var back = e.Item.Selected
                ? Color.FromArgb(70, 100, 140)
                : (e.ItemIndex % 2 == 0 ? ListBack : ListAlt);
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.ColumnIndex < _list.Columns.Count - 1)
            {
                using (var pen = new Pen(GridLine))
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font, e.Bounds, TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        public void SetBriefingItems(IList<NotamBriefingItem> items)
        {
            _items = items?.ToList() ?? new List<NotamBriefingItem>();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var item in _items)
            {
                var row = new ListViewItem(item.DisplayId);
                row.SubItems.Add(item.Type);
                row.SubItems.Add(Truncate(item.Title, 120));
                row.Tag = item;
                _list.Items.Add(row);
            }

            _list.EndUpdate();
            _lblHeader.Text = _items.Count == 0
                ? "Restriction briefing"
                : $"Restriction briefing ({_items.Count})";
            UpdateButtons();
        }

        public void SetSummary(RestrictionBriefingSummary summary)
        {
            if (summary == null)
                return;

            var parts = new List<string>();
            if (summary.TfrNotamCount > 0)
                parts.Add($"{summary.TfrNotamCount} TFR/NOTAM in view");
            if (summary.UasGridCells > 0)
                parts.Add($"{summary.UasGridCells} LAANC grid cells — click map or list");
            if (summary.SpecialUseAreas > 0)
                parts.Add($"{summary.SpecialUseAreas} special-use areas");
            if (summary.MetarCount > 0)
                parts.Add($"{summary.MetarCount} METARs");
            if (summary.SigmetCount > 0)
                parts.Add($"{summary.SigmetCount} SIGMETs");
            if (summary.GairmetCount > 0)
                parts.Add($"{summary.GairmetCount} G-AIRMETs");
            if (summary.PirepCount > 0)
                parts.Add($"{summary.PirepCount} PIREPs");
            if (summary.FlightSafetyWarnings > 0)
                parts.Add($"{summary.FlightSafetyWarnings} safety alerts");

            _lblSummary.Text = parts.Count == 0
                ? "No FAA graphic restrictions in the current map area."
                : string.Join(" · ", parts);
        }

        public void ClearBriefing()
        {
            SetBriefingItems(new List<NotamBriefingItem>());
            _lblSummary.Text = "Enable TFRs, NOTAMs, or LAANC grid to refresh this list.";
            _detail.Text = "Select a restriction to see details here.";
        }

        void ShowDetailForSelected() => ShowDetailForItem(SelectedItem());

        void ShowDetailForItem(NotamBriefingItem item)
        {
            if (item == null)
            {
                _detail.Text = "Select a restriction to see details here.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.DetailText))
            {
                _detail.Text = item.DetailText;
                return;
            }

            _detail.Text = item.Type + " — " + item.Title;
        }

        private void UpdateButtons()
        {
            var hasSelection = _list.SelectedItems.Count > 0;
            _btnZoom.Enabled = hasSelection;
            _btnOpen.Enabled = hasSelection && SelectedItem()?.DetailUrl != null;
        }

        private void InvokeSelected(Action<NotamBriefingItem> handler)
        {
            var item = SelectedItem();
            if (item == null)
                return;
            handler?.Invoke(item);
        }

        private NotamBriefingItem SelectedItem()
        {
            if (_list.SelectedItems.Count == 0)
                return null;
            return _list.SelectedItems[0].Tag as NotamBriefingItem;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text;
            return text.Substring(0, max - 1) + "…";
        }
    }
}
