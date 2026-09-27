using MissionPlanner.Utilities;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.Controls.PreFlight
{
    public partial class CheckListControl : UserControl
    {
        public List<CheckListItem> CheckListItems = new List<CheckListItem>();

        public string configfile;

        public string configfiledefault;

        public bool ArmingChecksPassed
        {
            get
            {
                lock (CheckListItems)
                {
                    try
                    {
                        return CheckListItems.Where(item => item.IsArmingBlocker)
                            .All(item => item.checkCond(item));
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        int rowcount = 0;

        //Controls and the lists for the controls
        private GroupBox gb = new GroupBox();
        private Label desc = new Label();
        private Label text = new Label();
        private CheckBox tickbox = new CheckBox();
        private List<GroupBox> groupboxes = new List<GroupBox>();
        private List<Label> descLabels = new List<Label>();
        private List<Label> labels = new List<Label>();
        private List<CheckBox> checkboxes = new List<CheckBox>();
        private List<Panel> statusIndicators = new List<Panel>();

        internal struct internaldata
        {
            internal Label desc;
            internal Label text;
            internal CheckBox tickbox;
            internal Panel statusIndicator;
            internal CheckListItem CLItem;
        }


        public CheckListControl()
            : this(Settings.GetUserDataDirectory() + "missionChecklist.xml",
                Settings.GetRunningDirectory() + "missionChecklistDefault.xml")
        {
        }

        public CheckListControl(string configfile, string configfiledefault)
        {
            this.configfile = configfile;
            this.configfiledefault = configfiledefault;
            InitializeComponent();

            try
            {
                MissionPlanner.Controls.PreFlight.CheckListItem.defaultsrc = MainV2.comPort.MAV.cs;

                LoadConfig();
            }
            catch
            {
                Console.WriteLine("Failed to read CheckList config file " + configfile);
            }

            timer1.Start();
        }

        public void Draw()
        {
            lock (this.CheckListItems)
            {
                if (rowcount == this.CheckListItems.Count)
                    return;

                panel1.Visible = false;
                panel1.Controls.Clear();

                int y = 0;

                rowcount = 0;
                groupboxes.Clear();
                descLabels.Clear();
                labels.Clear();
                checkboxes.Clear();
                statusIndicators.Clear();
                bool? automaticSection = null;
                var orderedItems = this.CheckListItems
                    .Where(item => item.ConditionType != CheckListItem.Conditional.NONE)
                    .Concat(this.CheckListItems.Where(item => item.ConditionType == CheckListItem.Conditional.NONE));
                foreach (var item in orderedItems)
                {
                    bool isAutomatic = item.ConditionType != CheckListItem.Conditional.NONE;
                    if (automaticSection != isAutomatic)
                    {
                        var section = addsectionlabel(5, y,
                            isAutomatic ? "AUTOMATIC CHECKS" : "MANUAL CHECKLIST");
                        y = section.Bottom;
                        automaticSection = isAutomatic;
                    }

                    var wrnctl = addwarningcontrol(5, y, item);

                    rowcount++;

                    y = wrnctl.Bottom;
                }
            }
            panel1.Visible = true;
        }

        void UpdateDisplay()
        {
            foreach (Control itemp in panel1.Controls)
            {
                foreach (Control item in itemp.Controls)
                {
                    if (item.Tag == null)
                        continue;

                    internaldata data = (internaldata)item.Tag;

                    if (item.Name.StartsWith("utext"))
                    {
                        item.Text = data.CLItem.DisplayText();
                        data.desc.Text = BlockerDescription(data.CLItem);
                    }
                    if (item.Name.StartsWith("utickbox"))
                    {
                        var tickbox = item as CheckBox;
                        if (data.CLItem.ConditionType != CheckListItem.Conditional.NONE)
                            tickbox.Checked = data.CLItem.checkCond(data.CLItem);

                        SetRowState(data, tickbox.Checked);
                    }
                    if (item.Name.StartsWith("uindicator"))
                        SetRowState(data, data.CLItem.checkCond(data.CLItem));
                }
            }
        }

        private void SetRowState(internaldata data, bool passed)
        {
            var color = passed ? data.CLItem._TrueColor : data.CLItem._FalseColor;
            data.text.ForeColor = color;
            data.desc.ForeColor = color;
            if (data.statusIndicator != null)
            {
                data.statusIndicator.ForeColor = passed ? Color.Green : Color.Red;
                data.statusIndicator.Invalidate();
            }
        }

        private Label addsectionlabel(int x, int y, string title)
        {
            var section = new Label
            {
                AutoSize = false,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(x, y + 4),
                Size = new Size(panel1.Width - 20, 24),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft
            };
            panel1.Controls.Add(section);
            return section;
        }

        Control addwarningcontrol(int x, int y, CheckListItem item, bool hideforchild = false)
        {
            var desctext = BlockerDescription(item);
            var texttext = item.DisplayText();

            var height = TextRenderer.MeasureText(desctext, this.Font).Height;

            var x0 = (int)(panel1.Width * 0.94);
            var x1 = (int)(x0 * 0.66);
            var x2 = (int)(x0 * 0.26);
            var x3 = (int)(x0 * 0.08);

            gb = new GroupBox() { Text = "", Location = new Point(x, y), Size = new Size(x0, 17 + height), Name = "gb" + y };

            desc = new Label() { Text = desctext, Location = new Point(5, 9), Size = new Size(x1, height), Name = "udesc" + y };
            text = new Label() { Text = texttext, Location = new Point(desc.Right, 9), Size = new Size(x2, height), Name = "utext" + y };
            tickbox = new CheckBox() { Checked = item.checkCond(item), Location = new Point((text.Right), 7), Size = new Size(21, 21), Name = "utickbox" + y };
            tickbox.Visible = item.ConditionType == CheckListItem.Conditional.NONE;
            var statusIndicator = new Panel
            {
                Location = new Point(text.Right, 9),
                Size = new Size(16, 16),
                Name = "uindicator" + y,
                Visible = item.ConditionType != CheckListItem.Conditional.NONE,
                ForeColor = item.checkCond(item) ? Color.Green : Color.Red
            };
            statusIndicator.Paint += (sender, args) =>
            {
                var indicator = sender as Panel;
                using (var brush = new SolidBrush(indicator.ForeColor))
                    args.Graphics.FillEllipse(brush, 1, 1, indicator.Width - 2, indicator.Height - 2);
            };
            tickbox.CheckedChanged += (sender, args) =>
            {
                if (item.ConditionType == CheckListItem.Conditional.NONE)
                {
                    item.ManualChecked = tickbox.Checked;
                    SaveConfig();
                }
            };

            var data = new internaldata
            {
                CLItem = item,
                desc = desc,
                text = text,
                tickbox = tickbox,
                statusIndicator = statusIndicator
            };
            desc.Tag = text.Tag = tickbox.Tag = statusIndicator.Tag = data;

            //Changing the font size of the desc labels text according to amount of characters contained in the label
            desc.TextAlign = ContentAlignment.MiddleLeft;
            if (desc.Text.ToCharArray().Length > 35)
            {
                if (desc.Text.ToCharArray().Length > 35 && desc.Text.ToCharArray().Length < 40)
                {
                    desc.Font = new Font(desc.Font.FontFamily, desc.Font.Size - 0.7f, desc.Font.Style);
                }
                else if (desc.Text.ToCharArray().Length >= 40)
                {
                    desc.Font = new Font(desc.Font.FontFamily, desc.Font.Size - 1.35f, desc.Font.Style);
                }
            }
            else if (desc.Text.ToCharArray().Length <= 35)
            {
                desc.Font = new Font(desc.Font.FontFamily, desc.Font.Size - 0.4f, desc.Font.Style);
            }

            //Add the controls to the main control
            gb.Controls.Add(desc);
            gb.Controls.Add(text);
            gb.Controls.Add(tickbox);
            gb.Controls.Add(statusIndicator);

            panel1.Controls.Add(gb);

            //Add controls to relevant control lists
            groupboxes.Add(gb);
            descLabels.Add(desc);
            labels.Add(text);
            checkboxes.Add(tickbox);
            statusIndicators.Add(statusIndicator);

            y = gb.Bottom;

            if (item.Child != null)
            {
                //return addwarningcontrol(x += 5, y, item.Child, true);
            }

            return gb;
        }

        /// <summary>Tick all manual arming-blocker checklist items (used by debug affirm).</summary>
        public void AffirmAllManualArmingItems()
        {
            lock (CheckListItems)
            {
                foreach (var item in CheckListItems)
                {
                    if (item == null || !item.IsArmingBlocker)
                        continue;
                    if (item.ConditionType == CheckListItem.Conditional.NONE)
                        item.ManualChecked = true;
                }

                rowcount = 0;
            }

            SaveConfig();
            Draw();
            UpdateDisplay();
        }

        public void LoadConfig()
        {
            string loadfile = configfile;
            var userFileName = Path.GetFileName(configfile);
            bool isLegacyUserFile =
                userFileName.Equals("checklist.xml", StringComparison.OrdinalIgnoreCase) ||
                userFileName.Equals("checklistDefault.xml", StringComparison.OrdinalIgnoreCase);

            if (isLegacyUserFile || !File.Exists(configfile))
            {
                if (!File.Exists(configfiledefault))
                {
                    return;
                }

                loadfile = configfiledefault;
            }

            LoadFromPath(loadfile);
            MergeMissingArmingBlockers(configfiledefault);
        }

        /// <summary>Load vehicle/stage-specific checklist template from default XML.</summary>
        public void ApplyStageChecklist(FlightOperationStage stage)
        {
            if (stage == FlightOperationStage.Unselected)
                return;

            var path = FlightPreflightProfiles.GetChecklistDefaultPath(stage,
                FlightPreflightProfiles.GetVehicleProfileKey());
            if (!File.Exists(path))
                return;

            configfiledefault = path;
            // The saved missionChecklist.xml must not hide the stage template.
            LoadFromPath(path);
            lock (CheckListItems)
                rowcount = 0;
            Draw();
        }

        static string BlockerDescription(CheckListItem item)
        {
            if (item == null)
                return "";
            return item.IsArmingBlocker ? item.Description + "  [blocks arm]" : item.Description;
        }

        void LoadFromPath(string loadfile)
        {
            if (string.IsNullOrEmpty(loadfile) || !File.Exists(loadfile))
                return;

            var reader = new System.Xml.Serialization.XmlSerializer(typeof(List<CheckListItem>),
                new Type[] { typeof(CheckListItem) });

            using (StreamReader sr = new StreamReader(loadfile))
            {
                CheckListItems = (List<CheckListItem>)reader.Deserialize(sr);
            }
        }

        void MergeMissingArmingBlockers(string templatePath)
        {
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
                return;

            List<CheckListItem> template;
            var reader = new System.Xml.Serialization.XmlSerializer(typeof(List<CheckListItem>),
                new Type[] { typeof(CheckListItem) });
            using (var sr = new StreamReader(templatePath))
                template = (List<CheckListItem>)reader.Deserialize(sr);

            if (template == null)
                return;

            lock (CheckListItems)
            {
                foreach (var item in template)
                {
                    if (item == null || !item.IsArmingBlocker)
                        continue;
                    var already = CheckListItems.Any(existing =>
                        existing != null &&
                        string.Equals(existing.Description, item.Description, StringComparison.OrdinalIgnoreCase));
                    if (!already)
                        CheckListItems.Add(item);
                }
            }
        }

        public void SaveConfig()
        {
            // save config
            System.Xml.Serialization.XmlSerializer writer =
                new System.Xml.Serialization.XmlSerializer(typeof(List<CheckListItem>),
                    new Type[] { typeof(CheckListItem), typeof(Color) });

            using (StreamWriter sw = new StreamWriter(configfile))
            {
                lock (CheckListItems)
                {
                    writer.Serialize(sw, CheckListItems);
                }
            }
        }

        private void BUT_edit_Click(object sender, EventArgs e)
        {
            CheckListEditor form = new CheckListEditor(this);
            form.Show();
            lock (this.CheckListItems)
                rowcount = 0;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            Draw();
            UpdateDisplay();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (!MainV2.DisplayConfiguration.displayPreFlightTabEdit)
            {
                BUT_edit.Visible = false;
            }

            if (Visible)
            {
                timer1.Enabled = true;
            }
            else
            {
                timer1.Enabled = false;
            }
        }

        public void Controls_Resize(object sender, EventArgs e)
        {
            // Guard against empty lists
            if (groupboxes.Count == 0 || descLabels.Count == 0 || labels.Count == 0 || checkboxes.Count == 0)
                return;

            //initialize controls x and y
            int gbsX = 0;
            int gbsY = 0;
            int lblsOneX = 0;
            int lblsOneY = 0;
            int lblsTwoX = 0;
            int lblsTwoY = 0;
            int cbxsX = 0;
            int cbxsY = 0;

            //Width of the controls
            //Panel1 variable width
            var panelOneWidth = panel1.Width;
            //Group boxes variable width
            var gbsWidth = groupboxes[0].Width = (int)(panelOneWidth * 0.9);
            //Desc label variable width
            var descLabelWidth = descLabels[0].Width = (int)(gbsWidth * 0.6204);
            //Second label variable width
            var labeTwolWidth = labels[0].Width = (int)(gbsWidth * 0.2444);
            //Checkbox variable width
            var checkboxWidth = checkboxes[0].Width = (int)(gbsWidth * 0.0752);

            //Setting Locations
            //Set the first groupboxes location
            groupboxes[0].Location = new Point(groupboxes[0].Location.X, groupboxes[0].Location.Y);
            gbsX = groupboxes[0].Location.X;
            gbsY = groupboxes[0].Location.Y;

            //location of the desc Label
            descLabels[0].Location = new Point(descLabels[0].Location.X, descLabels[0].Location.Y);
            lblsOneX = descLabels[0].Location.X;
            lblsOneY = descLabels[0].Location.Y;
            //set the second labels location
            var labelTwo = labels[0].Location = new Point(labels[0].Location.X, labels[0].Location.Y);
            lblsTwoX = descLabels[0].Width + (int)(gbsWidth * 0.02);
            lblsTwoY = labels[0].Location.Y;
            //set the first checkboxes location
            var checkboxOne = checkboxes[0].Location = new Point(checkboxes[0].Location.X, checkboxes[0].Location.Y);
            cbxsX = labels[0].Location.X + labels[0].Width + (int)(gbsWidth * 0.02);
            cbxsY = checkboxes[0].Location.Y;

            for (int i = 0; i < groupboxes.Count; i++)
            {
                if (groupboxes.Count > 0 && labels.Count > 0 && checkboxes.Count > 0)
                {
                    //Set the group box Y Location
                    gbsY = groupboxes[i].Bottom;
                    //Set the width of the desc labels
                    descLabelWidth = descLabels[i].Width;

                    if (i == 0)
                    {
                        //check the height of the panel1 = not to do with the change
                        var panelOneHeight = panel1.Height;

                        //ratio needs to be rechecked
                        if (this.panel1.Width > 0)
                        {
                            //set the width of the group boxes
                            groupboxes[i].Width = (int)(panelOneWidth * 0.9);

                            //desc Label X Location
                            lblsOneX = descLabels[i].Location.X;

                            //Second label X Location
                            lblsTwoX = labelTwo.X = (int)(groupboxes[i].Width * 0.66);

                            labels[i].Width = (int)(groupboxes[i].Width * 0.3);

                            //checkbox location
                            cbxsX = checkboxOne.X = (int)(groupboxes[i].Width * 0.9);
                        }
                    }
                    else if (i > 0)
                    {
                        //setting the groupboxes width
                        groupboxes[i].Width = (int)(panelOneWidth * 0.9);
                    }

                    //set the location in the group box for labels
                    descLabels[i].Location = new Point(lblsOneX, lblsOneY);
                    //set the location in the group box for the second labels
                    labels[i].Location = new Point(lblsTwoX, lblsTwoY);
                    //set the location in the group box for the checkboxes
                    checkboxes[i].Location = new Point(cbxsX, cbxsY);

                    //Bring controls to the front
                    groupboxes[i].BringToFront();
                    labels[i].BringToFront();
                    checkboxes[i].BringToFront();
                }
            }
        }

        private void CheckListControl_Load(object sender, EventArgs e)
        {
            this.Resize += new EventHandler(Controls_Resize);
        }
    }
}
