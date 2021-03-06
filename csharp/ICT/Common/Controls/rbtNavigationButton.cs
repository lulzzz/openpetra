//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop
//
// Copyright 2004-2010 by OM International
//
// This file is part of OpenPetra.org.
//
// OpenPetra.org is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// OpenPetra.org is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.
//
using System;
using System.Xml;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace Ict.Common.Controls
{
    /// <summary>
    /// this class shows a navigation button that can be checked, unchecked, and hovered over;
    /// it has an icon and a label;
    /// it has a gradient background
    /// </summary>
    public partial class TRbtNavigationButton : Owf.Controls.A1Panel
    {
        private static TOpenPetraMenuColours FOpenPetraMenuColours = new TOpenPetraMenuColours();

        private bool FChecked = false;
        private bool FHovering = false;
        private CheckedChanging FCheckChangingDelegate = null;

        /// <summary>
        /// constructor
        /// </summary>
        public TRbtNavigationButton(CheckedChanging ACheckChangingDelegate)
        {
            InitializeComponent();
            #region CATALOGI18N

            // this code has been inserted by GenerateI18N, all changes in this region will be overwritten by GenerateI18N
            #endregion

            this.BorderColor = FOpenPetraMenuColours.MenuBackgroundColour;
            this.GradientDirection = System.Drawing.Drawing2D.LinearGradientMode.Vertical;
            this.ShadowOffSet = 0;
            this.RoundCornerRadius = 0;
            this.ImageLocation = new System.Drawing.Point(7, 7);

            this.lblCaption.Enter += new System.EventHandler(this.PanelEnter);
            this.lblCaption.Leave += new System.EventHandler(this.PanelLeave);
            this.lblCaption.Click += new System.EventHandler(this.PanelClick);
            this.lblCaption.MouseLeave += new System.EventHandler(this.PanelLeave);
            this.lblCaption.MouseEnter += new System.EventHandler(this.PanelEnter);
            this.MouseLeave += new System.EventHandler(this.PanelLeave);
            this.MouseEnter += new System.EventHandler(this.PanelEnter);
            this.Enter += new System.EventHandler(this.PanelEnter);
            this.Leave += new System.EventHandler(this.PanelLeave);
            this.Click += new System.EventHandler(this.PanelClick);

            SetStandardAppearance();

            FCheckChangingDelegate = ACheckChangingDelegate;
        }

        /// Caption of the button
        public override string Text
        {
            get
            {
                return lblCaption.Text;
            }
            set
            {
                lblCaption.Text = value;
            }
        }

        /// <summary>
        /// checked state
        /// </summary>
        public bool Checked
        {
            get
            {
                return FChecked;
            }

            set
            {
                bool CheckChangeOK = true;

                if (FChecked != value)
                {
                    // This call returns false if the user does not have access to the module being selected.
                    // This is not determined by the Xml attribute but by a code look up using the Xml element name.
                    // This gets round the problem that it is possible for a client user to edit their copy of UINavigation and appear to give themselves
                    // permission for a module.  But note that it is still a client-side code solution and not a server-side one.
                    CheckChangeOK = TCommonControlsSecurity.CheckUserAccessToModuleUsingModuleElementName(((XmlNode)Tag).Name);

                    if ((FCheckChangingDelegate != null) && (CheckChangeOK))
                    {
                        // This call returns false if the selected module will be Finance and the user has no access to any ledger
                        // In that case the call shows a full message box explaining the situation - see lstFolderNavigation FolderCheckChanging
                        CheckChangeOK = FCheckChangingDelegate(this);
                    }

                    if (CheckChangeOK)
                    {
                        FChecked = value;

                        if (FChecked)
                        {
                            // uncheck all other sibling controls of type TRbtNavigationButton
                            foreach (Control sibling in Parent.Controls)
                            {
                                if ((sibling.GetType() == typeof(TRbtNavigationButton)) && (sibling != this))
                                {
                                    ((TRbtNavigationButton)sibling).Checked = false;
                                }
                            }

                            SetClickedAppearance();
                        }
                        else
                        {
                            SetStandardAppearance();
                        }

                        if (CheckedChanged != null)
                        {
                            CheckedChanged(this, null);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// set the path of the icon that should be displayed on this button
        /// </summary>
        public string Icon
        {
            set
            {
                Image = (new System.Drawing.Icon(value, 16, 16)).ToBitmap();
            }
        }

        /// <summary>
        /// Triggered when the Checked state is changing.
        /// </summary>
        public delegate bool CheckedChanging(TRbtNavigationButton ASender);

        /// <summary>
        /// Triggered when the Checked state has changed.
        /// </summary>
        public EventHandler CheckedChanged;

        /// mouse is hovering over button
        protected void PanelEnter(object sender, EventArgs e)
        {
            if (!FHovering)
            {
                FHovering = true;

                SetHoveringAppearance();
            }
        }

        /// mouse moves away from button
        protected void PanelLeave(object sender, EventArgs e)
        {
            if (FHovering)
            {
                FHovering = false;

                if (Checked)
                {
                    SetClickedAppearance();
                }
                else
                {
                    SetStandardAppearance();
                }
            }
        }

        /// mouse got clicked over button
        protected void PanelClick(object sender, EventArgs e)
        {
            Checked = true;
        }

        private void SetStandardAppearance()
        {
            this.GradientStartColor = FOpenPetraMenuColours.ToolStripGradientBegin;
            this.GradientEndColor = FOpenPetraMenuColours.ToolStripGradientEnd;

            Refresh();
        }

        private void SetClickedAppearance()
        {
            this.GradientStartColor = FOpenPetraMenuColours.ButtonSelectedGradientBegin;
            this.GradientEndColor = FOpenPetraMenuColours.ButtonPressedGradientEnd;

            Refresh();
        }

        private void SetHoveringAppearance()
        {
            this.GradientStartColor = FOpenPetraMenuColours.ButtonSelectedGradientBegin;
            this.GradientEndColor = FOpenPetraMenuColours.ButtonSelectedGradientEnd;

            Refresh();
        }
    }
}