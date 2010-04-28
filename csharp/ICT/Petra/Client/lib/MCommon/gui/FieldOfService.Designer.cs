/*
 * This file is automatically generated, but can also be edited with the Designer
 */

using System;
using System.Windows.Forms;
using System.ComponentModel;
using Ict.Common.Controls;
using Ict.Petra.Client.CommonControls;

namespace Ict.Petra.Client.MCommon
{
    partial class TFieldOfServiceWinForm
    {
        /// <summary>
        /// Designer variable used to keep track of non-visual components.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Disposes resources used by the form.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// This method is required for Windows Forms designer support.
        /// Do not change the method contents inside the source code editor. The Forms designer might
        /// not be able to load this method if it was changed manually.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources =
                new System.ComponentModel.ComponentResourceManager(typeof(TFieldOfServiceWinForm));

            this.lblPartnerKey = new System.Windows.Forms.Label();
            this.lblPartnerName = new System.Windows.Forms.Label();
            this.Label3 = new System.Windows.Forms.Label();
            this.Label4 = new System.Windows.Forms.Label();
            this.stbMain = new Ict.Common.Controls.TExtStatusBarHelp();
            this.SuspendLayout();

            //
            // lblPartnerKey
            //
            this.lblPartnerKey.Location = new System.Drawing.Point(112, 40);
            this.lblPartnerKey.Name = "lblPartnerKey";
            this.lblPartnerKey.TabIndex = 1;
            this.lblPartnerKey.Text = "PartnerKey";

            //
            // lblPartnerName
            //
            this.lblPartnerName.Location = new System.Drawing.Point(112, 64);
            this.lblPartnerName.Name = "lblPartnerName";
            this.lblPartnerName.Size = new System.Drawing.Size(264, 23);
            this.lblPartnerName.TabIndex = 3;
            this.lblPartnerName.Text = "Partner Name";

            //
            // Label3
            //
            this.Label3.Location = new System.Drawing.Point(8, 64);
            this.Label3.Name = "Label3";
            this.Label3.TabIndex = 2;
            this.Label3.Text = "Partner Name:";

            //
            // Label4
            //
            this.Label4.Location = new System.Drawing.Point(8, 40);
            this.Label4.Name = "Label4";
            this.Label4.TabIndex = 0;
            this.Label4.Text = "PartnerKey:";

            //
            // stbMain
            //
            this.stbMain.Location = new System.Drawing.Point(0, 264);
            this.stbMain.Name = "stbMain";
            this.stbMain.Size = new System.Drawing.Size(470, 22);
            this.stbMain.TabIndex = 4;

            //
            // TFieldOfServiceWinForm
            //
            this.AutoScaleBaseSize = new System.Drawing.Size(6, 14);
            this.ClientSize = new System.Drawing.Size(470, 286);
            this.Controls.Add(this.stbMain);
            this.Controls.Add(this.Label4);
            this.Controls.Add(this.Label3);
            this.Controls.Add(this.lblPartnerKey);
            this.Controls.Add(this.lblPartnerName);
            this.Name = "TFieldOfServiceWinForm";
            this.Text = "Field Of Service";
            this.Closing += new CancelEventHandler(this.TFieldOfServiceWinForm_Closing);
            this.Load += new System.EventHandler(this.TFieldOfServiceWinForm_Load);
            this.Controls.SetChildIndex(this.lblPartnerName, 0);
            this.Controls.SetChildIndex(this.lblPartnerKey, 0);
            this.Controls.SetChildIndex(this.Label3, 0);
            this.Controls.SetChildIndex(this.Label4, 0);
            this.ResumeLayout(false);
        }

        private Ict.Common.Controls.TExtStatusBarHelp stbMain;
        private System.Windows.Forms.Label lblPartnerKey;
        private System.Windows.Forms.Label lblPartnerName;
        private System.Windows.Forms.Label Label3;
        private System.Windows.Forms.Label Label4;
    }
}