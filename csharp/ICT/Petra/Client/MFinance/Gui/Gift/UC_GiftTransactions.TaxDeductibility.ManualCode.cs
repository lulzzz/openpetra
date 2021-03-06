//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop, christophert
//
// Copyright 2004-2012 by OM International
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
#region usings

using System;
using System.Reflection;

using Ict.Common;
using Ict.Common.Verification;

using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance;
using Ict.Petra.Shared.MFinance.Gift.Data;
using Ict.Petra.Shared.MFinance.Validation;
using Ict.Petra.Shared.MPartner.Partner.Data;

#endregion usings

namespace Ict.Petra.Client.MFinance.Gui.Gift
{
    public partial class TUC_GiftTransactions
    {
        #region event handler

        private void ToggleTaxDeductible(Object sender, EventArgs e)
        {
            if (FSETUseTaxDeductiblePercentageFlag)
            {
                bool taxDeductible = chkDetailTaxDeductible.Checked;

                EnableTaxDeductibilityPct(taxDeductible);

                if (taxDeductible)
                {
                    UpdateTaxDeductiblePct(Convert.ToInt64(txtDetailRecipientKey.Text), false);
                    ReconcileTaxDeductibleAmounts(FPreviouslySelectedDetailRow);
                }
                else
                {
                    txtDeductiblePercentage.NumberValueDecimal = 0.0m;
                    txtTaxDeductAmount.NumberValueDecimal = 0.0m;
                    txtNonDeductAmount.NumberValueDecimal = FPreviouslySelectedDetailRow.GiftTransactionAmount;
                }
            }
        }

        #endregion event handler

        #region control handling

        /// <summary>
        /// This is used to rearrnage the controls for when the Tax Deductibility Percentage is enabled (i.e. OM CH).
        /// It is unfortunately highly manual and might look confusing.
        /// But it seems to be the simpliest way to do this without writing a new screen specically for when this functionality is enabled.
        /// </summary>
        private void SetupTaxDeductibilityControls()
        {
            const int YSPACE = 25;
            const int YSPACE2 = YSPACE * 2;

            /* move standard controls */
            lblDetailMailingCode.Location = new System.Drawing.Point(lblDetailMailingCode.Location.X, lblDetailMailingCode.Location.Y + (YSPACE * 2));
            cmbDetailMailingCode.Location = new System.Drawing.Point(cmbDetailMailingCode.Location.X, cmbDetailMailingCode.Location.Y + (YSPACE * 2));

            lblDetailAccountCode.Location = new System.Drawing.Point(lblDetailAccountCode.Location.X, lblDetailAccountCode.Location.Y + YSPACE2);
            txtDetailAccountCode.Location = new System.Drawing.Point(txtDetailAccountCode.Location.X, txtDetailAccountCode.Location.Y + YSPACE2);
            txtDetailAccountCode.Width = 80;

            lblDetailGiftCommentOne.Location = new System.Drawing.Point(lblDetailGiftCommentOne.Location.X,
                lblDetailGiftCommentOne.Location.Y + YSPACE2);
            txtDetailGiftCommentOne.Location = new System.Drawing.Point(txtDetailGiftCommentOne.Location.X,
                txtDetailGiftCommentOne.Location.Y + YSPACE2);
            lblDetailCommentOneType.Location = new System.Drawing.Point(lblDetailCommentOneType.Location.X,
                lblDetailCommentOneType.Location.Y + YSPACE2);
            cmbDetailCommentOneType.Location = new System.Drawing.Point(cmbDetailCommentOneType.Location.X,
                cmbDetailCommentOneType.Location.Y + YSPACE2);
            lblDetailGiftCommentTwo.Location = new System.Drawing.Point(lblDetailGiftCommentTwo.Location.X,
                lblDetailGiftCommentTwo.Location.Y + YSPACE2);
            txtDetailGiftCommentTwo.Location = new System.Drawing.Point(txtDetailGiftCommentTwo.Location.X,
                txtDetailGiftCommentTwo.Location.Y + YSPACE2);
            lblDetailCommentTwoType.Location = new System.Drawing.Point(lblDetailCommentTwoType.Location.X,
                lblDetailCommentTwoType.Location.Y + YSPACE2);
            cmbDetailCommentTwoType.Location = new System.Drawing.Point(cmbDetailCommentTwoType.Location.X,
                cmbDetailCommentTwoType.Location.Y + YSPACE2);
            lblDetailGiftCommentThree.Location = new System.Drawing.Point(lblDetailGiftCommentThree.Location.X,
                lblDetailGiftCommentThree.Location.Y + YSPACE2);
            txtDetailGiftCommentThree.Location = new System.Drawing.Point(txtDetailGiftCommentThree.Location.X,
                txtDetailGiftCommentThree.Location.Y + YSPACE2);
            lblDetailCommentThreeType.Location = new System.Drawing.Point(lblDetailCommentThreeType.Location.X,
                lblDetailCommentThreeType.Location.Y + YSPACE2);
            cmbDetailCommentThreeType.Location = new System.Drawing.Point(cmbDetailCommentThreeType.Location.X,
                cmbDetailCommentThreeType.Location.Y + YSPACE2);

            grpDetailsForEachGift.Height += YSPACE2;

            // If pnlDetails is not big enough then move the splitter. Only want to move the splitter once as it's new location will be remembered.
            if (this.FindForm().Visible && pnlDetails.VerticalScroll.Visible)
            {
                sptTransactions.SplitterDistance -= YSPACE2;
            }

            /* move tax deductibility controls and make them visible */
            lblDeductiblePercentage.Location =
                new System.Drawing.Point(lblDeductiblePercentage.Location.X, lblDeductiblePercentage.Location.Y - (YSPACE * 7));
            txtDeductiblePercentage.Location =
                new System.Drawing.Point(txtDeductiblePercentage.Location.X, txtDeductiblePercentage.Location.Y - (YSPACE * 7));
            lblDeductiblePercentage.Visible = true;
            txtDeductiblePercentage.Visible = true;
            txtDeductiblePercentage.NumberValueDecimal = 100;
            txtDeductiblePercentage.NegativeValueAllowed = false;

            lblTaxDeductAmount.Location = new System.Drawing.Point(lblTaxDeductAmount.Location.X - 5, lblTaxDeductAmount.Location.Y - (YSPACE * 5));
            lblTaxDeductAmount.Width = lblTaxDeductAmount.Width + 10;
            txtTaxDeductAmount.Location = new System.Drawing.Point(txtTaxDeductAmount.Location.X, txtTaxDeductAmount.Location.Y - (YSPACE * 5));
            lblTaxDeductAmount.Visible = true;
            txtTaxDeductAmount.Visible = true;
            txtTaxDeductAmount.CurrencyCode = FBatchRow.CurrencyCode;
            lblTaxDeductAmount.Text = Catalog.GetString("Tax Deductible Amt:");
            lblNonDeductAmount.Location = new System.Drawing.Point(lblNonDeductAmount.Location.X - 6, lblNonDeductAmount.Location.Y - (YSPACE * 3));
            lblNonDeductAmount.Width = lblNonDeductAmount.Width + 11;
            txtNonDeductAmount.Location = new System.Drawing.Point(txtNonDeductAmount.Location.X, txtNonDeductAmount.Location.Y - (YSPACE * 3));
            lblNonDeductAmount.Visible = true;
            txtNonDeductAmount.Visible = true;
            txtNonDeductAmount.CurrencyCode = FBatchRow.CurrencyCode;
            lblNonDeductAmount.Text = Catalog.GetString("Non Deductible Amt:");

            lblDeductibleAccount.Location = new System.Drawing.Point(lblDeductibleAccount.Location.X, lblDeductibleAccount.Location.Y - (YSPACE * 5));
            txtDeductibleAccount.Location = new System.Drawing.Point(txtDeductibleAccount.Location.X, txtDeductibleAccount.Location.Y - (YSPACE * 5));
            lblDeductibleAccount.Visible = true;
            txtDeductibleAccount.Visible = true;
            txtDeductibleAccount.Width = txtDetailAccountCode.Width;

            /* add event */
            txtDeductiblePercentage.TextChanged += UpdateTaxDeductibilityAmounts;

            /* add extra column to grid */
            grdDetails.AddTextColumn(Catalog.GetString("% Tax Deductibility"), FMainDS.AGiftDetail.ColumnTaxDeductiblePct);

            /* fix tab order */
            int STARTINGINDEX = txtDetailGiftTransactionAmount.TabIndex + 20;
            txtDeductiblePercentage.TabIndex = STARTINGINDEX;
            txtTaxDeductAmount.TabIndex = STARTINGINDEX += 20;
            txtNonDeductAmount.TabIndex = STARTINGINDEX += 20;
            cmbDetailMotivationGroupCode.TabIndex = STARTINGINDEX += 20;
            cmbMotivationDetailCode.TabIndex = STARTINGINDEX += 20;
            cmbDetailMailingCode.TabIndex = STARTINGINDEX += 20;
            txtDetailCostCentreCode.TabIndex = STARTINGINDEX += 20;
            txtDeductibleAccount.TabIndex = STARTINGINDEX += 20;
            txtDetailAccountCode.TabIndex = STARTINGINDEX += 20;
            txtDetailGiftCommentOne.TabIndex = STARTINGINDEX += 20;
            cmbDetailCommentOneType.TabIndex = STARTINGINDEX += 20;
            txtDetailGiftCommentTwo.TabIndex = STARTINGINDEX += 20;
            cmbDetailCommentTwoType.TabIndex = STARTINGINDEX += 20;
            txtDetailGiftCommentThree.TabIndex = STARTINGINDEX += 20;
            cmbDetailCommentThreeType.TabIndex = STARTINGINDEX += 20;

            if (FMainDS.AGiftDetail != null)
            {
                FValidationControlsDict.Add(FMainDS.AGiftDetail.Columns[(short)FMainDS.AGiftDetail.GetType().GetField("ColumnTaxDeductiblePctId",
                                                                            BindingFlags.Public | BindingFlags.Static |
                                                                            BindingFlags.FlattenHierarchy).GetValue(FMainDS.AGiftDetail.GetType())],
                    new TValidationControlsData(txtDeductiblePercentage, Catalog.GetString("% Tax Deductible")));
            }
        }

        // show tax deductible percentage data in controls
        private void ShowTaxDeductibleManual(GiftBatchTDSAGiftDetailRow ARow)
        {
            if (ARow.IsTaxDeductiblePctNull())
            {
                txtDeductiblePercentage.NumberValueDecimal = 0;
            }
            else
            {
                txtDeductiblePercentage.NumberValueDecimal = ARow.TaxDeductiblePct;
            }

            if (ARow.IsTaxDeductibleAccountCodeNull())
            {
                txtDeductibleAccount.Text = string.Empty;
            }
            else
            {
                txtDeductibleAccount.Text = ARow.TaxDeductibleAccountCode;
            }

            EnableTaxDeductibilityPct(ARow.TaxDeductible);
            UpdateTaxDeductibilityAmounts(this, null);
        }

        private void EnableTaxDeductibilityPct(bool AEnable)
        {
            txtDeductiblePercentage.Enabled = AEnable;
        }

        #endregion control handling

        #region data handling

        // get tax deductible percentage data from controls
        private void GetTaxDeductibleDataFromControlsManual(GiftBatchTDSAGiftDetailRow ARow)
        {
            if (FNewGiftInProcess)
            {
                return;
            }

            ARow.TaxDeductiblePct = txtDeductiblePercentage.NumberValueDecimal.Value;

            ReconcileTaxDeductibleAmounts(ARow);

            if (txtDeductibleAccount.Text.Length == 0)
            {
                ARow.SetTaxDeductibleAccountCodeNull();
            }
            else
            {
                ARow.TaxDeductibleAccountCode = txtDeductibleAccount.Text;
            }
        }

        private void ReconcileTaxDeductibleAmounts(GiftBatchTDSAGiftDetailRow ARow)
        {
            AGiftDetailRow GDR = (AGiftDetailRow)ARow;

            TaxDeductibility.UpdateTaxDeductibiltyAmounts(ref GDR);

            if ((txtTaxDeductAmount.NumberValueDecimal != ARow.TaxDeductibleAmount)
                || (txtNonDeductAmount.NumberValueDecimal != ARow.NonDeductibleAmount))
            {
                txtTaxDeductAmount.NumberValueDecimal = ARow.TaxDeductibleAmount;
                txtNonDeductAmount.NumberValueDecimal = ARow.NonDeductibleAmount;
            }
        }

        // Set the Tax Deductibility Percentage from a Recipient's PPartnerTaxDeductiblePct row (if it exists)
        private void UpdateTaxDeductiblePct(Int64 APartnerKey, bool ARecipientChanged)
        {
            if (APartnerKey == 0)
            {
                txtDeductiblePercentage.NumberValueDecimal = txtDeductiblePercentage.Enabled ? 100.0m : 0.0m;
                return;
            }

            // 100% default if tax deductibility is not limited
            decimal TaxDeductiblePct = 100.0m;

            if (ARecipientChanged)
            {
                FMainDS.PPartnerTaxDeductiblePct.Clear();
                FMainDS.PPartnerTaxDeductiblePct.Merge(TRemote.MFinance.Gift.WebConnectors.LoadPartnerTaxDeductiblePct(APartnerKey));
            }

            if (chkDetailTaxDeductible.Checked && txtDeductiblePercentage.Enabled
                && (FMainDS.PPartnerTaxDeductiblePct != null) && (FMainDS.PPartnerTaxDeductiblePct.Rows.Count > 0))
            {
                foreach (PPartnerTaxDeductiblePctRow Row in FMainDS.PPartnerTaxDeductiblePct.Rows)
                {
                    // if no valid records exist then the recipient has not limited tax deductible by default
                    if ((Row.PartnerKey == APartnerKey) && (Row.DateValidFrom <= dtpDateEntered.Date))
                    {
                        TaxDeductiblePct = Row.PercentageTaxDeductible;
                    }
                }
            }
            else if (!chkDetailTaxDeductible.Checked || !txtDeductiblePercentage.Enabled)
            {
                TaxDeductiblePct = 0.0m;
            }

            txtDeductiblePercentage.NumberValueDecimal = TaxDeductiblePct;
        }

        // Set the Tax Deductibility Percentage from a Recipient's PPartnerTaxDeductiblePct row (if it exists)
        private decimal RetrieveTaxDeductiblePct(Int64 APartnerKey, bool ATaxDeductible)
        {
            // 100% default if tax deductibility is not limited
            decimal TaxDeductiblePct = 100;

            if (APartnerKey > 0)
            {
                if (ATaxDeductible)
                {
                    FMainDS.PPartnerTaxDeductiblePct.Clear();
                    FMainDS.PPartnerTaxDeductiblePct.Merge(TRemote.MFinance.Gift.WebConnectors.LoadPartnerTaxDeductiblePct(APartnerKey));

                    if ((FMainDS.PPartnerTaxDeductiblePct != null) && (FMainDS.PPartnerTaxDeductiblePct.Rows.Count > 0))
                    {
                        foreach (PPartnerTaxDeductiblePctRow Row in FMainDS.PPartnerTaxDeductiblePct.Rows)
                        {
                            // if no valid records exist then the recipient has not limited tax deductible by default
                            if ((Row.PartnerKey == APartnerKey) && (Row.DateValidFrom <= dtpDateEntered.Date))
                            {
                                TaxDeductiblePct = Row.PercentageTaxDeductible;
                            }
                        }
                    }
                }
                else
                {
                    TaxDeductiblePct = 0.0m;
                }
            }
            else
            {
                TaxDeductiblePct = ATaxDeductible ? 100.0m : 0.0m;
            }

            return TaxDeductiblePct;
        }

        #endregion data handling

        #region data validation

        private void ValidateTaxDeductiblePct(GiftBatchTDSAGiftDetailRow ARow, ref TVerificationResultCollection AVerificationResultCollection)
        {
            int PreValidationResultCount = AVerificationResultCollection.Count;

            TSharedFinanceValidation_Gift.ValidateTaxDeductiblePct(this, ARow, ref AVerificationResultCollection, FValidationControlsDict);

            //No validation errors occurred
            if (PreValidationResultCount == AVerificationResultCollection.Count)
            {
                ReconcileTaxDeductibleAmounts(ARow);
            }
        }

        #endregion data validation
    }
}