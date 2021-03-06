//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       timop
//
// Copyright 2004-2017 by OM International
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
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using GNU.Gettext;

using Ict.Common;
using Ict.Common.Controls;
using Ict.Common.Data;
using Ict.Common.Verification;

using Ict.Petra.Client.App.Core;
using Ict.Petra.Client.App.Core.RemoteObjects;
using Ict.Petra.Client.CommonForms;
using Ict.Petra.Client.MFinance.Logic;
using Ict.Petra.Client.CommonDialogs;

using Ict.Petra.Shared;
using Ict.Petra.Shared.MFinance;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MFinance.GL.Data;
using Ict.Petra.Shared.MFinance.Validation;

using SourceGrid;

#region changelog

/*
 * Fix unhandled exception when posting/testing batch containing analysis attributes: https://tracker.openpetra.org/view.php?id=5562 - Moray
 */
#endregion

namespace Ict.Petra.Client.MFinance.Gui.GL
{
    public partial class TUC_GLTransactions : IBoundImageEvaluator
    {
        /// <summary>
        /// The batch to which the currently viewed transactions belong
        /// </summary>
        public Int32 FBatchNumber = -1;

        private Int32 FLedgerNumber = -1;
        private Int32 FJournalNumber = -1;
        private Int32 FTransactionNumber = -1;
        private bool FLoadCompleted = false;
        private bool FActiveOnly = true;
        private string FTransactionCurrency = string.Empty;

        private decimal FDebitAmount = 0;
        private decimal FCreditAmount = 0;

        private ABatchRow FBatchRow = null;
        private AAccountTable FAccountList = null;
        private ACostCentreTable FCostCentreList = null;

        private GLSetupTDS FCacheDS = null;
        private GLBatchTDSAJournalRow FJournalRow = null;
        private ATransAnalAttribRow FPSAttributesRow = null;
        private TAnalysisAttributes FAnalysisAttributesLogic;

        private SourceGrid.Cells.Editors.ComboBox FcmbAnalAttribValues;

        private bool FShowStatusDialogOnLoad = true;

        private bool FIsUnposted = true;
        private string FBatchStatus = string.Empty;
        private string FJournalStatus = string.Empty;
        private bool FDoneComboInitialise = false;
        private bool FContainsSystemGenerated = false;
        private bool FSuppressListChanged = false;

        /// <summary>
        /// Sets a flag to show the status dialog when transactions are loaded
        /// </summary>
        public Boolean ShowStatusDialogOnLoad
        {
            set
            {
                FShowStatusDialogOnLoad = value;
            }
        }

        private void InitialiseControls()
        {
            cmbDetailKeyMinistryKey.ComboBoxWidth = txtDetailNarrative.Width;
        }

        /// <summary>
        /// WorkAroundInitialization
        /// </summary>
        public void WorkAroundInitialization()
        {
            txtCreditAmount.Validated += new EventHandler(ControlHasChanged);
            txtDebitAmount.Validated += new EventHandler(ControlHasChanged);
            cmbDetailCostCentreCode.Validated += new EventHandler(ControlValidatedHandler);
            cmbDetailAccountCode.Validated += new EventHandler(ControlValidatedHandler);
            cmbDetailKeyMinistryKey.Validated += new EventHandler(ControlValidatedHandler);
            txtDetailNarrative.Validated += new EventHandler(ControlValidatedHandler);
            txtDetailReference.Validated += new EventHandler(ControlValidatedHandler);
            grdAnalAttributes.Selection.SelectionChanged += new RangeRegionChangedEventHandler(AnalysisAttributesGrid_RowSelected);
            dtpDetailTransactionDate.Validated += new EventHandler(ControlValidatedHandler);

            //Disallow the entry of the minus sign as no negative amounts allowed.
            //Instead, the user is expected to follow accounting riles and apply a positive amount
            //  to debit or credit accordingly to achieve the same effect
            txtDebitAmount.NegativeValueAllowed = false;
            txtCreditAmount.NegativeValueAllowed = false;
        }

        /// <summary>
        /// Get current transaction row
        /// </summary>
        /// <returns></returns>
        public GLBatchTDSATransactionRow GetCurrentTransactionRow()
        {
            return (GLBatchTDSATransactionRow) this.GetSelectedDetailRow();
        }

        /// <summary>
        /// load the transactions into the grid
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AJournalNumber"></param>
        /// <param name="ACurrencyCode"></param>
        /// <param name="AFromBatchTab"></param>
        /// <param name="ABatchStatus"></param>
        /// <param name="AJournalStatus"></param>
        /// <returns>True if new GL transactions were loaded, false if transactions had been loaded already.</returns>
        public bool LoadTransactions(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            Int32 AJournalNumber,
            string ACurrencyCode,
            bool AFromBatchTab = false,
            string ABatchStatus = MFinanceConstants.BATCH_UNPOSTED,
            string AJournalStatus = MFinanceConstants.BATCH_UNPOSTED)
        {
            TFrmStatusDialog dlgStatus = null;
            bool DifferentBatchSelected = false;

            FLoadCompleted = false;

            FBatchRow = SelectedBatchRow();
            FJournalRow = GetJournalRow();

            if ((FBatchRow == null) || (FJournalRow == null))
            {
                return false;
            }

            //FBatchNumber and FJournalNumber may have already been set outside
            //  so need to reset to previous value
            if ((txtBatchNumber.Text.Length > 0) && (FBatchNumber.ToString() != txtBatchNumber.Text))
            {
                FBatchNumber = Int32.Parse(txtBatchNumber.Text);
            }

            if ((txtJournalNumber.Text.Length > 0) && (FJournalNumber.ToString() != txtJournalNumber.Text))
            {
                FJournalNumber = Int32.Parse(txtJournalNumber.Text);
            }

            bool FirstRun = (FLedgerNumber != ALedgerNumber);
            bool BatchChanged = (FBatchNumber != ABatchNumber);
            bool BatchStatusChanged = (!BatchChanged && (FBatchStatus != ABatchStatus));
            bool JournalChanged = (BatchChanged || (FJournalNumber != AJournalNumber));
            bool JournalStatusChanged = (!JournalChanged && (FJournalStatus != AJournalStatus));
            bool CurrencyChanged = (FTransactionCurrency != ACurrencyCode);

            if (FirstRun)
            {
                FLedgerNumber = ALedgerNumber;
            }

            if (BatchChanged)
            {
                FBatchNumber = ABatchNumber;
            }

            if (BatchStatusChanged)
            {
                FBatchStatus = ABatchStatus;
            }

            if (JournalChanged)
            {
                FJournalNumber = AJournalNumber;
            }

            if (JournalStatusChanged)
            {
                FJournalStatus = AJournalStatus;
            }

            if (CurrencyChanged)
            {
                FTransactionCurrency = ACurrencyCode;
            }

            FIsUnposted = (FBatchRow.BatchStatus == MFinanceConstants.BATCH_UNPOSTED);

            if (FirstRun)
            {
                InitialiseControls();
            }

            try
            {
                this.Cursor = Cursors.WaitCursor;

                //Check if the same batch and journal is selected, so no need to apply filter
                if (!FirstRun
                    && !BatchChanged
                    && !JournalChanged
                    && !BatchStatusChanged
                    && !JournalStatusChanged
                    && !CurrencyChanged
                    && (FMainDS.ATransaction.DefaultView.Count > 0))
                {
                    //Same as previously selected batch and journal

                    //Need to reconnect FPrev in some circumstances
                    if (FPreviouslySelectedDetailRow == null)
                    {
                        DataRowView rowView = (DataRowView)grdDetails.Rows.IndexToDataSourceRow(FPrevRowChangedRow);

                        if (rowView != null)
                        {
                            FPreviouslySelectedDetailRow = (GLBatchTDSATransactionRow)(rowView.Row);
                        }
                    }

                    if (FIsUnposted && (GetSelectedRowIndex() > 0))
                    {
                        if (AFromBatchTab)
                        {
                            SelectRowInGrid(GetSelectedRowIndex());
                        }
                        else
                        {
                            GetDetailsFromControls(FPreviouslySelectedDetailRow);
                        }
                    }

                    FLoadCompleted = true;

                    return false;
                }

                // Different batch selected
                DifferentBatchSelected = true;
                bool requireControlSetup = (FLedgerNumber == -1) || (CurrencyChanged);

                //Handle dialog
                dlgStatus = new TFrmStatusDialog(FPetraUtilsObject.GetForm());

                if (FShowStatusDialogOnLoad == true)
                {
                    dlgStatus.Show();
                    FShowStatusDialogOnLoad = false;
                    dlgStatus.Heading = String.Format(Catalog.GetString("Batch {0}, Journal {1}"), ABatchNumber, AJournalNumber);
                    dlgStatus.CurrentStatus = Catalog.GetString("Loading transactions ...");
                }

                FLedgerNumber = ALedgerNumber;
                FBatchNumber = ABatchNumber;
                FJournalNumber = AJournalNumber;
                FTransactionNumber = -1;
                FTransactionCurrency = ACurrencyCode;
                FBatchStatus = ABatchStatus;
                FJournalStatus = AJournalStatus;

                FPreviouslySelectedDetailRow = null;
                grdDetails.SuspendLayout();
                //Empty grids before filling them
                grdDetails.DataSource = null;
                grdAnalAttributes.DataSource = null;
                FSuppressListChanged = false;

                // This sets the main part of the filter but excluding the additional items set by the user GUI
                // It gets the right sort order
                SetTransactionDefaultView();

                //Set the Analysis attributes filter as well
                FAnalysisAttributesLogic = new TAnalysisAttributes(FLedgerNumber, FBatchNumber, FJournalNumber);
                FAnalysisAttributesLogic.SetTransAnalAttributeDefaultView(FMainDS);
                FMainDS.ATransAnalAttrib.DefaultView.AllowNew = false;

                //Load from server if necessary
                if (FMainDS.ATransaction.DefaultView.Count == 0)
                {
                    dlgStatus.CurrentStatus = Catalog.GetString("Requesting transactions from server...");
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransactionAndRelatedTablesForJournal(ALedgerNumber, ABatchNumber,
                            AJournalNumber));
                }
                else if (FMainDS.ATransAnalAttrib.DefaultView.Count == 0) // just in case transactions have been loaded in a separate process without analysis attributes
                {
                    dlgStatus.CurrentStatus = Catalog.GetString("Requesting analysis attributes from server...");
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransAnalAttribForJournal(ALedgerNumber, ABatchNumber, AJournalNumber));
                }

                FContainsSystemGenerated = ((TFrmGLBatch) this.ParentForm).BatchIsAutoGenerated(FBatchRow);

                //We need to call this because we have not called ShowData(), which would have set it.
                // This differs from the Gift screen.
                grdDetails.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ATransaction.DefaultView);

                // Now we set the full filter
                dlgStatus.CurrentStatus = Catalog.GetString("Selecting the records...");
                FFilterAndFindObject.ApplyFilter();

                dlgStatus.CurrentStatus = Catalog.GetString("Configuring analysis attributes ...");

                if (grdAnalAttributes.Columns.Count == 1)
                {
                    grdAnalAttributes.SpecialKeys = GridSpecialKeys.Default | GridSpecialKeys.Tab;

                    FcmbAnalAttribValues = new SourceGrid.Cells.Editors.ComboBox(typeof(string));
                    FcmbAnalAttribValues.EnableEdit = true;
                    FcmbAnalAttribValues.EditableMode = EditableMode.Focus;
                    grdAnalAttributes.AddTextColumn(Catalog.GetString("Value"),
                        FMainDS.ATransAnalAttrib.Columns[ATransAnalAttribTable.GetAnalysisAttributeValueDBName()], 150,
                        FcmbAnalAttribValues);
                    FcmbAnalAttribValues.Control.SelectedValueChanged += new EventHandler(this.AnalysisAttributeValueChanged);

                    grdAnalAttributes.Columns[0].Width = 99;
                }

                grdAnalAttributes.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ATransAnalAttrib.DefaultView);
                grdAnalAttributes.SetHeaderTooltip(0, Catalog.GetString("Type"));
                grdAnalAttributes.SetHeaderTooltip(1, Catalog.GetString("Value"));

                // if this form is readonly or batch is posted, then we need all account and cost centre codes, because old codes might have been used
                bool ActiveOnly = false; //(this.Enabled && FIsUnposted && !FContainsSystemGenerated);

                if (requireControlSetup || (FActiveOnly != ActiveOnly))
                {
                    FActiveOnly = ActiveOnly;

                    //Load all analysis attribute values
                    if (FCacheDS == null)
                    {
                        dlgStatus.CurrentStatus = Catalog.GetString("Loading analysis attributes ...");
                        FCacheDS = TRemote.MFinance.GL.WebConnectors.LoadAAnalysisAttributes(FLedgerNumber, FActiveOnly);
                    }

                    SetupExtraGridFunctionality();

                    dlgStatus.CurrentStatus = Catalog.GetString("Initialising accounts and cost centres ...");

                    // We suppress change detection because these are the correct values
                    // Then initialise our combo boxes for the correct account codes and cost centres
                    bool prevSuppressChangeDetection = FPetraUtilsObject.SuppressChangeDetection;
                    FPetraUtilsObject.SuppressChangeDetection = true;
                    TFinanceControls.InitialiseAccountList(ref cmbDetailAccountCode, FLedgerNumber, true, false, ActiveOnly, false,
                        ACurrencyCode, true);
                    TFinanceControls.InitialiseCostCentreList(ref cmbDetailCostCentreCode, FLedgerNumber, true, false, ActiveOnly, false, true);
                    FPetraUtilsObject.SuppressChangeDetection = prevSuppressChangeDetection;

                    cmbDetailCostCentreCode.AttachedLabel.Text = TFinanceControls.SELECT_VALID_COST_CENTRE;
                    cmbDetailAccountCode.AttachedLabel.Text = TFinanceControls.SELECT_VALID_ACCOUNT;
                }

                UpdateTransactionTotals();
                grdDetails.ResumeLayout();
                FLoadCompleted = true;

                ShowData();
                SelectRowInGrid(1);
                ShowDetails(); //Needed because of how currency is handled
                UpdateChangeableStatus();

                UpdateRecordNumberDisplay();
                FFilterAndFindObject.SetRecordNumberDisplayProperties();

                //Check for missing analysis attributes and their values
                if (FIsUnposted && (grdDetails.Rows.Count > 1))
                {
                    string updatedTransactions = string.Empty;

                    dlgStatus.CurrentStatus = Catalog.GetString("Checking analysis attributes ...");
                    FAnalysisAttributesLogic.ReconcileTransAnalysisAttributes(FMainDS, FCacheDS, out updatedTransactions);

                    if (updatedTransactions.Length > 0)
                    {
                        //Remove trailing comma
                        updatedTransactions = updatedTransactions.Remove(updatedTransactions.Length - 2);
                        MessageBox.Show(String.Format(Catalog.GetString(
                                    "Analysis Attributes have been updated in transaction(s): {0}.{1}{1}Remember to set their values before posting!"),
                                updatedTransactions,
                                Environment.NewLine),
                            "Analysis Attributes",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        FPetraUtilsObject.SetChangedFlag();
                    }
                }

                RefreshAnalysisAttributesGrid();
            }
            catch (Exception ex)
            {
                TLogging.LogException(ex, Utilities.GetMethodSignature());
                throw;
            }
            finally
            {
                if (dlgStatus != null)
                {
                    dlgStatus.Close();
                }

                this.Cursor = Cursors.Default;
            }

            return DifferentBatchSelected;
        }

        private void SetTransactionDefaultView(bool AAscendingOrder = true)
        {
            string sort = AAscendingOrder ? "ASC" : "DESC";

            if (FBatchNumber != -1)
            {
                string rowFilter = String.Format("{0}={1} AND {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    FJournalNumber);

                FMainDS.ATransaction.DefaultView.RowFilter = rowFilter;
                FFilterAndFindObject.FilterPanelControls.SetBaseFilter(rowFilter, true);
                FFilterAndFindObject.CurrentActiveFilter = rowFilter;
                // We don't apply the filter yet!

                FMainDS.ATransaction.DefaultView.Sort = String.Format("{0} " + sort,
                    ATransactionTable.GetTransactionNumberDBName());
            }
        }

        private ABatchRow SelectedBatchRow()
        {
            return ((TFrmGLBatch)ParentForm).GetBatchControl().GetSelectedDetailRow();
        }

        /// <summary>
        /// get the details of the current journal
        /// </summary>
        /// <returns></returns>
        private GLBatchTDSAJournalRow GetJournalRow()
        {
            return ((TFrmGLBatch)ParentForm).GetJournalsControl().GetSelectedDetailRow();
        }

        /// <summary>
        /// add a new transactions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void NewRow(System.Object sender, EventArgs e)
        {
            FPetraUtilsObject.VerificationResultCollection.Clear();

            if (CreateNewATransaction())
            {
                pnlTransAnalysisAttributes.Enabled = true;
                btnDeleteAll.Enabled = btnDelete.Enabled && (FFilterAndFindObject.IsActiveFilterEqualToBase) && !FContainsSystemGenerated;

                //Needs to be called at end of addition process to process Analysis Attributes
                AccountCodeDetailChanged(null, null);
            }
        }

        /// <summary>
        /// make sure the correct transaction number is assigned and the journal.lastTransactionNumber is updated
        /// </summary>
        /// <param name="ANewRow">returns the modified new transaction row</param>
        /// <param name="ARefJournalRow">Can be null; otherwise the journal to which the transaction will belong</param>
        public void NewRowManual(ref GLBatchTDSATransactionRow ANewRow, AJournalRow ARefJournalRow = null)
        {
            if (ARefJournalRow == null)
            {
                ARefJournalRow = FJournalRow;
            }

            ANewRow.LedgerNumber = ARefJournalRow.LedgerNumber;
            ANewRow.BatchNumber = ARefJournalRow.BatchNumber;
            ANewRow.JournalNumber = ARefJournalRow.JournalNumber;
            ANewRow.TransactionNumber = ++ARefJournalRow.LastTransactionNumber;
            ANewRow.TransactionDate = SelectedBatchRow().DateEffective;

            if (FPreviouslySelectedDetailRow != null)
            {
                ANewRow.CostCentreCode = FPreviouslySelectedDetailRow.CostCentreCode;

                // Don't copy these over if previous transaction was a reversal
                if (!FPreviouslySelectedDetailRow.SystemGenerated)
                {
                    ANewRow.Narrative = FPreviouslySelectedDetailRow.Narrative;
                    ANewRow.Reference = FPreviouslySelectedDetailRow.Reference;
                }
            }
        }

        /// <summary>
        /// show ledger, batch and journal number
        /// </summary>
        private void ShowDataManual()
        {
            if (FLedgerNumber != -1)
            {
                string TransactionCurrency = FJournalRow.TransactionCurrency;
                string BaseCurrency = FMainDS.ALedger[0].BaseCurrency;

                txtLedgerNumber.Text = TFinanceControls.GetLedgerNumberAndName(FLedgerNumber);
                txtBatchNumber.Text = FBatchRow.BatchNumber.ToString();
                txtJournalNumber.Text = FJournalNumber.ToString();

                lblTransactionCurrency.Text = String.Format(Catalog.GetString("{0} (Transaction Currency)"), TransactionCurrency);
                txtDebitAmount.CurrencyCode = TransactionCurrency;
                txtCreditAmount.CurrencyCode = TransactionCurrency;
                txtCreditTotalAmount.CurrencyCode = TransactionCurrency;
                txtDebitTotalAmount.CurrencyCode = TransactionCurrency;

                lblBaseCurrency.Text = String.Format(Catalog.GetString("{0} (Base Currency)"), BaseCurrency);
                txtDebitAmountBase.CurrencyCode = BaseCurrency;
                txtCreditAmountBase.CurrencyCode = BaseCurrency;
                txtCreditTotalAmountBase.CurrencyCode = BaseCurrency;
                txtDebitTotalAmountBase.CurrencyCode = BaseCurrency;

                // foreign currency accounts only get transactions in that currency
                if (FTransactionCurrency != TransactionCurrency)
                {
                    string SelectedAccount = cmbDetailAccountCode.GetSelectedString();

                    // if this form is readonly, then we need all account and cost centre codes, because old codes might have been used
                    bool ActiveOnly = this.Enabled;

                    TFinanceControls.InitialiseAccountList(ref cmbDetailAccountCode, FLedgerNumber,
                        true, false, ActiveOnly, false, TransactionCurrency);

                    cmbDetailAccountCode.SetSelectedString(SelectedAccount, -1);

                    FTransactionCurrency = TransactionCurrency;
                }
            }
        }

        private void ShowDetailsManual(ATransactionRow ARow)
        {
            grdDetails.TabStop = (ARow != null);
            grdAnalAttributes.Enabled = (ARow != null);

            if (ARow == null)
            {
                FTransactionNumber = -1;
                ClearControls();
                btnNew.Focus();
                return;
            }

            FTransactionNumber = ARow.TransactionNumber;

            if (ARow.DebitCreditIndicator)
            {
                txtDebitAmount.NumberValueDecimal = ARow.TransactionAmount;
                txtCreditAmount.NumberValueDecimal = 0;
                txtDebitAmountBase.NumberValueDecimal = ARow.AmountInBaseCurrency;
                txtCreditAmountBase.NumberValueDecimal = 0;
            }
            else
            {
                txtDebitAmount.NumberValueDecimal = 0;
                txtCreditAmount.NumberValueDecimal = ARow.TransactionAmount;
                txtDebitAmountBase.NumberValueDecimal = 0;
                txtCreditAmountBase.NumberValueDecimal = ARow.AmountInBaseCurrency;
            }

            RefreshAnalysisAttributesGrid();
            UpdateChangeableStatus();
        }

        private void RefreshAnalysisAttributesGrid()
        {
            // We can be called when the user has clicked on the current transaction during an attribute value edit.
            // If the attributes are stuck in an edit we must not fiddle with the grid but wait until the edit is complete.
            if (grdAnalAttributes.IsEditorEditing)
            {
                return;
            }

            //Empty the grid
            FMainDS.ATransAnalAttrib.DefaultView.RowFilter = "1=2";
            FPSAttributesRow = null;

            if ((FPreviouslySelectedDetailRow == null)
                || (!pnlTransAnalysisAttributes.Enabled && FIsUnposted)
                || !TRemote.MFinance.Setup.WebConnectors.AccountHasAnalysisAttributes(FLedgerNumber, cmbDetailAccountCode.GetSelectedString(),
                    FActiveOnly))
            {
                if (grdAnalAttributes.Enabled)
                {
                    grdAnalAttributes.Enabled = false;
                    lblAnalAttributesHelp.Enabled = false;
                }

                return;
            }
            else
            {
                if (!grdAnalAttributes.Enabled)
                {
                    grdAnalAttributes.Enabled = true;
                }
            }

            FAnalysisAttributesLogic.SetTransAnalAttributeDefaultView(FMainDS, FTransactionNumber);

            grdAnalAttributes.DataSource = new DevAge.ComponentModel.BoundDataView(FMainDS.ATransAnalAttrib.DefaultView);

            bool gotRows = (grdAnalAttributes.Rows.Count > 1);
            lblAnalAttributesHelp.Enabled = gotRows;

            if (gotRows)
            {
                grdAnalAttributes.SelectRowWithoutFocus(1);
                AnalysisAttributesGrid_RowSelected(null, null);
            }
        }

        private void AnalysisAttributesGrid_RowSelected(System.Object sender, RangeRegionChangedEventArgs e)
        {
            if (grdAnalAttributes.Selection.ActivePosition.IsEmpty() || (grdAnalAttributes.Selection.ActivePosition.Column == 0))
            {
                return;
            }

            if ((TAnalysisAttributes.GetSelectedAttributeRow(grdAnalAttributes) == null)
                || (FPSAttributesRow == TAnalysisAttributes.GetSelectedAttributeRow(grdAnalAttributes)))
            {
                return;
            }

            FPSAttributesRow = TAnalysisAttributes.GetSelectedAttributeRow(grdAnalAttributes);

            string currentAnalTypeCode = FPSAttributesRow.AnalysisTypeCode;

            if (FIsUnposted)
            {
                FCacheDS.AFreeformAnalysis.DefaultView.RowFilter = String.Format("{0}='{1}' AND {2}=true",
                    AFreeformAnalysisTable.GetAnalysisTypeCodeDBName(),
                    currentAnalTypeCode,
                    AFreeformAnalysisTable.GetActiveDBName());
            }
            else
            {
                FCacheDS.AFreeformAnalysis.DefaultView.RowFilter = String.Format("{0}='{1}'",
                    AFreeformAnalysisTable.GetAnalysisTypeCodeDBName(),
                    currentAnalTypeCode);
            }

            int analTypeCodeValuesCount = FCacheDS.AFreeformAnalysis.DefaultView.Count;

            if (analTypeCodeValuesCount == 0)
            {
                MessageBox.Show(Catalog.GetString(
                        "No attribute values are defined!"), currentAnalTypeCode, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            string[] analTypeValues = new string[analTypeCodeValuesCount];

            FCacheDS.AFreeformAnalysis.DefaultView.Sort = AFreeformAnalysisTable.GetAnalysisValueDBName();
            int counter = 0;

            foreach (DataRowView dvr in FCacheDS.AFreeformAnalysis.DefaultView)
            {
                AFreeformAnalysisRow faRow = (AFreeformAnalysisRow)dvr.Row;
                analTypeValues[counter] = faRow.AnalysisValue;

                counter++;
            }

            //Refresh the combo values
            FcmbAnalAttribValues.StandardValuesExclusive = true;
            FcmbAnalAttribValues.StandardValues = analTypeValues;
        }

        private void AnalysisAttributeValueChanged(System.Object sender, EventArgs e)
        {
            if (!FIsUnposted)
            {
                return;
            }

            DevAge.Windows.Forms.DevAgeComboBox valueType = (DevAge.Windows.Forms.DevAgeComboBox)sender;

            int selectedValueIndex = valueType.SelectedIndex;

            if (selectedValueIndex < 0)
            {
                return;
            }
            else if ((FPSAttributesRow != null)
                     && (valueType.Items[selectedValueIndex].ToString() != FPSAttributesRow.AnalysisAttributeValue.ToString()))
            {
                FPetraUtilsObject.SetChangedFlag();
            }
        }

        private void GetDetailDataFromControlsManual(ATransactionRow ARow)
        {
            if (ARow == null)
            {
                return;
            }

            Decimal OldTransactionAmount = ARow.TransactionAmount;
            bool OldDebitCreditIndicator = ARow.DebitCreditIndicator;

            GetDataForAmountFields(ARow);

            if ((OldTransactionAmount != Convert.ToDecimal(ARow.TransactionAmount))
                || (OldDebitCreditIndicator != ARow.DebitCreditIndicator))
            {
                // Third set true because we are already inside BeginEdit/EndEdit
                UpdateTransactionTotals(TGLBatchEnums.eGLLevel.Transaction, false, true);
            }
        }

        private void GetDataForAmountFields(ATransactionRow ARow)
        {
            if (ARow == null)
            {
                return;
            }

            bool DebitCreditIndicator;
            decimal TransactionAmount;

            if ((txtDebitAmount.Text.Length == 0) && (txtDebitAmount.NumberValueDecimal.Value != 0))
            {
                txtDebitAmount.NumberValueDecimal = 0;
            }

            if ((txtCreditAmount.Text.Length == 0) && (txtCreditAmount.NumberValueDecimal.Value != 0))
            {
                txtCreditAmount.NumberValueDecimal = 0;
            }

            DebitCreditIndicator = (txtDebitAmount.NumberValueDecimal.Value > 0);

            if (ARow.DebitCreditIndicator != DebitCreditIndicator)
            {
                ARow.DebitCreditIndicator = DebitCreditIndicator;
            }

            if (ARow.DebitCreditIndicator)
            {
                TransactionAmount = Math.Abs(txtDebitAmount.NumberValueDecimal.Value);

                if (txtCreditAmount.NumberValueDecimal.Value != 0)
                {
                    txtCreditAmount.NumberValueDecimal = 0;
                }
            }
            else
            {
                TransactionAmount = Math.Abs(txtCreditAmount.NumberValueDecimal.Value);
            }

            if (ARow.TransactionAmount != TransactionAmount)
            {
                ARow.TransactionAmount = TransactionAmount;
            }
        }

        private Boolean EnsureGLDataPresent(Int32 ALedgerNumber,
            Int32 ABatchNumber,
            Int32 AJournalNumber,
            ref DataView AJournalDV,
            bool AUpdateCurrentTransOnly)
        {
            bool RetVal = false;

            DataView TransDV = new DataView(FMainDS.ATransaction);

            if (AUpdateCurrentTransOnly)
            {
                AJournalDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    AJournalTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    AJournalTable.GetJournalNumberDBName(),
                    AJournalNumber);

                RetVal = true;
            }
            else if (AJournalNumber == 0) //need to load all journals and data
            {
                AJournalDV.RowFilter = String.Format("{0}={1} And {2}='{3}'",
                    AJournalTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    AJournalTable.GetJournalStatusDBName(),
                    MFinanceConstants.BATCH_UNPOSTED);

                if (AJournalDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadAJournalAndRelatedTablesForBatch(ALedgerNumber, ABatchNumber));

                    if (AJournalDV.Count == 0)
                    {
                        return false;
                    }
                }
                else
                {
                    TransDV.RowFilter = String.Format("{0}={1}",
                        ATransactionTable.GetBatchNumberDBName(),
                        ABatchNumber);

                    if (TransDV.Count == 0)
                    {
                        //Load transactions and analysis attributes for entire batch
                        FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransactionAndRelatedTablesForBatch(ALedgerNumber, ABatchNumber));
                    }
                }

                //As long as transactions exist, return true
                RetVal = true;
            }
            else
            {
                AJournalDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    AJournalTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    AJournalTable.GetJournalNumberDBName(),
                    AJournalNumber);

                if (AJournalDV.Count == 0)
                {
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadAJournalAndRelatedTables(ALedgerNumber, ABatchNumber, AJournalNumber));

                    if (AJournalDV.Count == 0)
                    {
                        return false;
                    }
                }

                TransDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    AJournalNumber);

                if (TransDV.Count == 0)
                {
                    //Load transactions and analysis attributes
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransactionAndRelatedTablesForJournal(ALedgerNumber, ABatchNumber,
                            AJournalNumber));
                }

                RetVal = true;
            }

            return RetVal;
        }

        /// <summary>
        /// Checks various things on the form before saving
        /// </summary>
        public void CheckBeforeSaving()
        {
        }

        /// <summary>
        ///  update amount in other currencies (optional) and recalculate all totals for current batch and journal
        /// </summary>
        /// <param name="AUpdateLevel"></param>
        /// <param name="AUpdateTransDates"></param>
        /// <param name="AIsActionInsideRowEdit">Set this to true if the call is from within a BeginEdit/EndEdit clause</param>
        public void UpdateTransactionTotals(TGLBatchEnums.eGLLevel AUpdateLevel = TGLBatchEnums.eGLLevel.Transaction,
            bool AUpdateTransDates = false, bool AIsActionInsideRowEdit = false)
        {
            bool TransactionRowActive = false;
            bool TransactionDataChanged = false;
            bool JournalDataChanged = false;

            int CurrentTransBatchNumber = FBatchNumber;
            int CurrentTransJournalNumber = FJournalNumber;
            int CurrentTransNumber = 0;

            int CurrentJournalNumber = 0;

            bool BatchLevel = (AUpdateLevel == TGLBatchEnums.eGLLevel.Batch);
            bool JournalLevel = (AUpdateLevel == TGLBatchEnums.eGLLevel.Journal);
            bool TransLevel = (AUpdateLevel == TGLBatchEnums.eGLLevel.Transaction);

            if (AUpdateLevel == TGLBatchEnums.eGLLevel.Analysis)
            {
                TLogging.Log(String.Format("{0} called with wrong first argument!", Utilities.GetMethodSignature()));
                return;
            }

            ABatchRow CurrentBatchRow = SelectedBatchRow();
            AJournalRow CurrentJournalRow = null;

            if (CurrentBatchRow == null)
            {
                return;
            }

            bool UnpostedBatch = CurrentBatchRow.BatchStatus == MFinanceConstants.BATCH_UNPOSTED;

            //Set inital values after confirming not null
            Boolean OriginalSaveButtonState = FPetraUtilsObject.HasChanges;
            String LedgerBaseCurrency = FMainDS.ALedger[0].BaseCurrency;
            String LedgerIntlCurrency = FMainDS.ALedger[0].IntlCurrency;
            Int32 LedgerNumber = CurrentBatchRow.LedgerNumber;
            Int32 CurrentBatchNumber = CurrentBatchRow.BatchNumber;

            DataView JournalsToUpdateDV = new DataView(FMainDS.AJournal);
            DataView TransactionsToUpdateDV = new DataView(FMainDS.ATransaction);

            //If called at the batch level, clear the current selections
            if (BatchLevel)
            {
                FPetraUtilsObject.SuppressChangeDetection = true;
                ClearCurrentSelection();
                ((TFrmGLBatch) this.ParentForm).GetJournalsControl().ClearCurrentSelection();
                FPetraUtilsObject.SuppressChangeDetection = false;
                //Ensure that when the Journal and Trans tab is opened, the data is reloaded.
            }
            else
            {
                CurrentJournalRow = GetJournalRow();
                CurrentJournalNumber = CurrentJournalRow.JournalNumber;
            }

            if (JournalLevel)
            {
                ClearCurrentSelection();
            }
            else if (TransLevel && (FPreviouslySelectedDetailRow != null))
            {
                TransactionRowActive = true;

                CurrentTransBatchNumber = FPreviouslySelectedDetailRow.BatchNumber;
                CurrentTransJournalNumber = FPreviouslySelectedDetailRow.JournalNumber;
                CurrentTransNumber = FPreviouslySelectedDetailRow.TransactionNumber;
            }

            //Get the corporate exchange rate
            ((TFrmGLBatch) this.ParentForm).WarnAboutMissingIntlExchangeRate = false;
            Decimal IntlRateToBaseCurrency = ((TFrmGLBatch) this.ParentForm).GetInternationalCurrencyExchangeRate();

            if (!EnsureGLDataPresent(LedgerNumber, CurrentBatchNumber, CurrentJournalNumber, ref JournalsToUpdateDV, TransactionRowActive))
            {
                // No transactions to process
                return;
            }

            //Iterate through journals - we will be updating the current row directly
            if ((FPreviouslySelectedDetailRow != null) && !AIsActionInsideRowEdit)
            {
                FPreviouslySelectedDetailRow.BeginEdit();
            }

            decimal AmtCreditTotal = 0.0M;
            decimal AmtDebitTotal = 0.0M;
            decimal AmtCreditTotalBase = 0.0M;
            decimal AmtDebitTotalBase = 0.0M;
            bool currentRowEdited = false;

            foreach (DataRowView drv in JournalsToUpdateDV)
            {
                GLBatchTDSAJournalRow journal = (GLBatchTDSAJournalRow)drv.Row;

                Boolean journalIsInIntlCurrency = (journal.TransactionCurrency == LedgerIntlCurrency);
                Boolean journalIsReval = (journal.TransactionTypeCode == CommonAccountingTransactionTypesEnum.REVAL.ToString());

                if (BatchLevel)
                {
                    //Journal row is active
                    if (journal.DateEffective != CurrentBatchRow.DateEffective)
                    {
                        journal.DateEffective = CurrentBatchRow.DateEffective;
                    }
                }

                TransactionsToUpdateDV.RowFilter = String.Format("{0}={1} And {2}={3}",
                    ATransactionTable.GetBatchNumberDBName(),
                    journal.BatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    journal.JournalNumber);

                if (TransactionsToUpdateDV.Count == 0)   // journal is empty
                {
                    if ((txtCreditAmountBase.NumberValueDecimal != 0)
                        || (txtCreditAmount.NumberValueDecimal != 0)
                        || (txtDebitAmountBase.NumberValueDecimal != 0)
                        || (txtDebitAmount.NumberValueDecimal != 0)
                        || (txtCreditTotalAmount.NumberValueDecimal != 0)
                        || (txtDebitTotalAmount.NumberValueDecimal != 0)
                        || (txtCreditTotalAmountBase.NumberValueDecimal != 0)
                        || (txtDebitTotalAmountBase.NumberValueDecimal != 0))
                    {
                        txtCreditAmountBase.NumberValueDecimal = 0;
                        txtCreditAmount.NumberValueDecimal = 0;
                        txtDebitAmountBase.NumberValueDecimal = 0;
                        txtDebitAmount.NumberValueDecimal = 0;
                        txtCreditTotalAmount.NumberValueDecimal = 0;
                        txtDebitTotalAmount.NumberValueDecimal = 0;
                        txtCreditTotalAmountBase.NumberValueDecimal = 0;
                        txtDebitTotalAmountBase.NumberValueDecimal = 0;
                    }
                }

                // NOTE: AlanP changed this code in Feb 2015.  Before that the code used the DataView directly.
                // We did a foreach on the DataView and modified the international currency amounts in the rows of the DataRowView.
                // Amazingly the effect of this was that each iteration of the loop took longer and longer.  We had a set of 70 transactions
                // that we worked with and the first row took 50ms and then the times increased linearly until row 70 took 410ms!
                // Overall the 70 rows CONSISTENTLY took just over 15 seconds.  But the scary thing was that if we had, say, 150 rows (which
                // would easily be possible), we would be looking at more than 1 minute to execute this loop.
                // So the code now converts the view to a Working Table, then operates on the data in that table and finally merges the
                // working table back into FMainDS.  By doing this the time for the 70 rows goes from 15 seconds to 300ms.

                ATransactionTable transactionsTemp = new ATransactionTable();
                transactionsTemp.Merge(TransactionsToUpdateDV.ToTable());

                // Iterate through all transactions in this Journal
                // I'm going backwards because I may want to delete this row.
                for (int i = transactionsTemp.Rows.Count - 1; i >= 0; i--)
                {
                    ATransactionRow workingRow = transactionsTemp[i];

                    // Only if an unposted batch (this updates the database)
                    if (UnpostedBatch)
                    {
                        TransactionDataChanged = false;
                        workingRow.BeginEdit();

                        bool IsCurrentActiveTransRow = (TransactionRowActive
                                                        && workingRow.BatchNumber == CurrentTransBatchNumber
                                                        && workingRow.JournalNumber == CurrentTransJournalNumber
                                                        && workingRow.TransactionNumber == CurrentTransNumber
                                                        );

                        if (AUpdateTransDates
                            && (workingRow.TransactionDate != CurrentBatchRow.DateEffective))
                        {
                            workingRow.TransactionDate = CurrentBatchRow.DateEffective;

                            if (IsCurrentActiveTransRow)
                            {
                                FPreviouslySelectedDetailRow.TransactionDate = CurrentBatchRow.DateEffective;
                            }
                            else
                            {
                                TransactionDataChanged = true;
                            }
                        }

                        if (IsCurrentActiveTransRow)
                        {
                            if (FPreviouslySelectedDetailRow.DebitCreditIndicator)
                            {
                                if ((txtCreditAmountBase.NumberValueDecimal != 0)
                                    || (txtCreditAmount.NumberValueDecimal != 0)
                                    || (FPreviouslySelectedDetailRow.TransactionAmount != Convert.ToDecimal(txtDebitAmount.NumberValueDecimal)))
                                {
                                    txtCreditAmountBase.NumberValueDecimal = 0;
                                    txtCreditAmount.NumberValueDecimal = 0;
                                    FPreviouslySelectedDetailRow.TransactionAmount = Convert.ToDecimal(txtDebitAmount.NumberValueDecimal);
                                    currentRowEdited = true;
                                }
                            }
                            else
                            {
                                if ((txtDebitAmountBase.NumberValueDecimal != 0)
                                    || (txtDebitAmount.NumberValueDecimal != 0)
                                    || (FPreviouslySelectedDetailRow.TransactionAmount != Convert.ToDecimal(txtCreditAmount.NumberValueDecimal)))
                                {
                                    txtDebitAmountBase.NumberValueDecimal = 0;
                                    txtDebitAmount.NumberValueDecimal = 0;
                                    FPreviouslySelectedDetailRow.TransactionAmount = Convert.ToDecimal(txtCreditAmount.NumberValueDecimal);
                                    currentRowEdited = true;
                                }
                            }
                        }

                        // Recalculate the amount in base currency
                        if (!journalIsReval)
                        {
                            Decimal AmtInBaseCurrency = GLRoutines.Divide(
                                workingRow.TransactionAmount, journal.ExchangeRateToBase);

                            if (AmtInBaseCurrency != workingRow.AmountInBaseCurrency)
                            {
                                workingRow.AmountInBaseCurrency = AmtInBaseCurrency;

                                if (IsCurrentActiveTransRow)
                                {
                                    FPreviouslySelectedDetailRow.AmountInBaseCurrency = AmtInBaseCurrency;
                                }
                                else
                                {
                                    TransactionDataChanged = true;
                                }
                            }

                            if (journalIsInIntlCurrency)
                            {
                                if (workingRow.AmountInIntlCurrency != workingRow.TransactionAmount)
                                {
                                    workingRow.AmountInIntlCurrency = workingRow.TransactionAmount;

                                    if (IsCurrentActiveTransRow)
                                    {
                                        FPreviouslySelectedDetailRow.AmountInIntlCurrency = workingRow.TransactionAmount;
                                    }
                                    else
                                    {
                                        TransactionDataChanged = true;
                                    }
                                }
                            }
                            else
                            {
                                // TODO: Instead of hard coding the number of decimals to 2 (for US cent) it should come from the database.
                                Decimal AmtInIntlCurrency =
                                    (IntlRateToBaseCurrency ==
                                     0) ? 0 : GLRoutines.Divide(workingRow.AmountInBaseCurrency,
                                        IntlRateToBaseCurrency,
                                        2);

                                if (workingRow.AmountInIntlCurrency != AmtInIntlCurrency)
                                {
                                    workingRow.AmountInIntlCurrency = AmtInIntlCurrency;

                                    if (IsCurrentActiveTransRow)
                                    {
                                        FPreviouslySelectedDetailRow.AmountInIntlCurrency = AmtInIntlCurrency;
                                    }
                                    else
                                    {
                                        TransactionDataChanged = true;
                                    }
                                }
                            }
                        }

                        if (workingRow.DebitCreditIndicator)
                        {
                            AmtDebitTotal += workingRow.TransactionAmount;
                            AmtDebitTotalBase += workingRow.AmountInBaseCurrency;

                            if (IsCurrentActiveTransRow)
                            {
                                if ((FPreviouslySelectedDetailRow.AmountInBaseCurrency != workingRow.AmountInBaseCurrency)
                                    || (FPreviouslySelectedDetailRow.AmountInIntlCurrency != workingRow.AmountInIntlCurrency)
                                    || (txtDebitAmountBase.NumberValueDecimal != workingRow.AmountInBaseCurrency)
                                    || (txtCreditAmountBase.NumberValueDecimal != 0))
                                {
                                    FPreviouslySelectedDetailRow.AmountInBaseCurrency = workingRow.AmountInBaseCurrency;
                                    FPreviouslySelectedDetailRow.AmountInIntlCurrency = workingRow.AmountInIntlCurrency;
                                    txtDebitAmountBase.NumberValueDecimal = workingRow.AmountInBaseCurrency;
                                    txtCreditAmountBase.NumberValueDecimal = 0;
                                    currentRowEdited = true;
                                }
                            }
                        }
                        else
                        {
                            AmtCreditTotal += workingRow.TransactionAmount;
                            AmtCreditTotalBase += workingRow.AmountInBaseCurrency;

                            if (IsCurrentActiveTransRow)
                            {
                                if ((FPreviouslySelectedDetailRow.AmountInBaseCurrency != workingRow.AmountInBaseCurrency)
                                    || (FPreviouslySelectedDetailRow.AmountInIntlCurrency != workingRow.AmountInIntlCurrency)
                                    || (txtCreditAmountBase.NumberValueDecimal != workingRow.AmountInBaseCurrency)
                                    || (txtDebitAmountBase.NumberValueDecimal != 0))
                                {
                                    FPreviouslySelectedDetailRow.AmountInBaseCurrency = workingRow.AmountInBaseCurrency;
                                    FPreviouslySelectedDetailRow.AmountInIntlCurrency = workingRow.AmountInIntlCurrency;
                                    txtCreditAmountBase.NumberValueDecimal = workingRow.AmountInBaseCurrency;
                                    txtDebitAmountBase.NumberValueDecimal = 0;
                                    currentRowEdited = true;
                                }
                            }
                        }

                        if (TransactionDataChanged)
                        {
                            workingRow.EndEdit();
                        }
                        else
                        {
                            workingRow.CancelEdit();
                            workingRow.Delete();
                        }
                    }
                    else // e.g. a posted batch
                    {
                        if (workingRow.DebitCreditIndicator)
                        {
                            AmtDebitTotal += workingRow.TransactionAmount;
                            AmtDebitTotalBase += workingRow.AmountInBaseCurrency;
                        }
                        else
                        {
                            AmtCreditTotal += workingRow.TransactionAmount;
                            AmtCreditTotalBase += workingRow.AmountInBaseCurrency;
                        }
                    }
                }   // Next transaction

                // only make changes if unposted
                if ((transactionsTemp.Rows.Count > 0) && UnpostedBatch)
                {
                    FMainDS.ATransaction.Merge(transactionsTemp);
                    JournalDataChanged = true;
                }

                if (TransactionRowActive
                    && (journal.BatchNumber == CurrentTransBatchNumber)
                    && (journal.JournalNumber == CurrentTransJournalNumber)
                    && ((txtCreditTotalAmount.NumberValueDecimal != AmtCreditTotal)
                        || (txtDebitTotalAmount.NumberValueDecimal != AmtDebitTotal)
                        || (txtCreditTotalAmountBase.NumberValueDecimal != AmtCreditTotalBase)
                        || (txtDebitTotalAmountBase.NumberValueDecimal != AmtDebitTotalBase)))
                {
                    txtCreditTotalAmount.NumberValueDecimal = AmtCreditTotal;
                    txtDebitTotalAmount.NumberValueDecimal = AmtDebitTotal;
                    txtCreditTotalAmountBase.NumberValueDecimal = AmtCreditTotalBase;
                    txtDebitTotalAmountBase.NumberValueDecimal = AmtDebitTotalBase;
                }
            }   // next journal

            if (currentRowEdited)
            {
                FPreviouslySelectedDetailRow.EndEdit();
            }
            else if ((FPreviouslySelectedDetailRow != null) && !AIsActionInsideRowEdit)
            {
                FPreviouslySelectedDetailRow.CancelEdit();
            }

            //Update totals of Batch
            if (UnpostedBatch)
            {
                GLRoutines.UpdateBatchTotals(ref FMainDS, ref CurrentBatchRow);
            }

            //In trans loading
            txtCreditTotalAmount.NumberValueDecimal = AmtCreditTotal;
            txtDebitTotalAmount.NumberValueDecimal = AmtDebitTotal;
            txtCreditTotalAmountBase.NumberValueDecimal = AmtCreditTotalBase;
            txtDebitTotalAmountBase.NumberValueDecimal = AmtDebitTotalBase;

            // refresh the currency symbols
            if (TransactionRowActive)
            {
                ShowDataManual();
            }

            if (!JournalDataChanged && (OriginalSaveButtonState != FPetraUtilsObject.HasChanges))
            {
                ((TFrmGLBatch)ParentForm).SaveChanges();
            }
            else if (JournalDataChanged)
            {
                // Automatically save the changes to Totals??
                // For now we will just enable the save button which will give the user a surprise!
                FPetraUtilsObject.SetChangedFlag();
            }
        }

        private void SetupAccountCostCentreVariables(int ALedgerNumber)
        {
            DataTable CostCentreListTable = TDataCache.TMFinance.GetCacheableFinanceTable(TCacheableFinanceTablesEnum.CostCentreList, ALedgerNumber);

            ACostCentreTable tmpCostCentreTable = new ACostCentreTable();

            FMainDS.Tables.Add(tmpCostCentreTable);
            DataUtilities.ChangeDataTableToTypedDataTable(ref CostCentreListTable, FMainDS.Tables[tmpCostCentreTable.TableName].GetType(), "");
            FMainDS.RemoveTable(tmpCostCentreTable.TableName);

            if ((CostCentreListTable == null) || (CostCentreListTable.Rows.Count == 0))
            {
                FCostCentreList = null;
            }
            else
            {
                FCostCentreList = (ACostCentreTable)CostCentreListTable;
            }

            DataTable AccountListTable = TDataCache.TMFinance.GetCacheableFinanceTable(TCacheableFinanceTablesEnum.AccountList, ALedgerNumber);

            AAccountTable tmpAccountTable = new AAccountTable();
            FMainDS.Tables.Add(tmpAccountTable);
            DataUtilities.ChangeDataTableToTypedDataTable(ref AccountListTable, FMainDS.Tables[tmpAccountTable.TableName].GetType(), "");
            FMainDS.RemoveTable(tmpAccountTable.TableName);

            if ((AccountListTable == null) || (AccountListTable.Rows.Count == 0))
            {
                FAccountList = null;
            }
            else
            {
                FAccountList = (AAccountTable)AccountListTable;
            }
        }

        private void SetupExtraGridFunctionality()
        {
            SetupAccountCostCentreVariables(FLedgerNumber);

            //Add conditions to columns
            int indexOfCostCentreCodeDataColumn = 2;
            int indexOfAccountCodeDataColumn = 3;

            // Add red triangle to inactive accounts
            grdDetails.AddAnnotationImage(this, indexOfCostCentreCodeDataColumn,
                BoundGridImage.AnnotationContextEnum.CostCentreCode, BoundGridImage.DisplayImageEnum.Inactive);
            grdDetails.AddAnnotationImage(this, indexOfAccountCodeDataColumn,
                BoundGridImage.AnnotationContextEnum.AccountCode, BoundGridImage.DisplayImageEnum.Inactive);

            //Add conditions to columns of Analysis Attributes grid
            int indexOfAnalysisCodeColumn = 0;
            int indexOfAnalysisAttributeValueColumn = 1;

            // Add red triangle to inactive analysis type codes and their values
            grdAnalAttributes.AddAnnotationImage(this, indexOfAnalysisCodeColumn,
                BoundGridImage.AnnotationContextEnum.AnalysisTypeCode, BoundGridImage.DisplayImageEnum.Inactive);
            grdAnalAttributes.AddAnnotationImage(this, indexOfAnalysisAttributeValueColumn,
                BoundGridImage.AnnotationContextEnum.AnalysisAttributeValue, BoundGridImage.DisplayImageEnum.Inactive);
        }

        private bool AccountIsActive(int ALedgerNumber, string AAccountCode = "")
        {
            bool AccountActive = false;
            bool AccountExists = true;

            //If empty, read value from combo
            if (AAccountCode.Length == 0)
            {
                if ((cmbDetailAccountCode.SelectedIndex != -1)
                    && (cmbDetailAccountCode.Count > 0)
                    && (cmbDetailAccountCode.GetSelectedString() != null))
                {
                    AAccountCode = cmbDetailAccountCode.GetSelectedString();
                }
                else
                {
                    return false;
                }
            }

            if (FAccountList == null)
            {
                SetupAccountCostCentreVariables(ALedgerNumber);
            }

            AccountActive = TFinanceControls.AccountIsActive(ALedgerNumber, AAccountCode, FAccountList, out AccountExists);

            if (!AccountExists && (AAccountCode.Length > 0))
            {
                string errorMessage = String.Format(Catalog.GetString("Account {0} does not exist in Ledger {1}!"),
                    AAccountCode,
                    ALedgerNumber);
                TLogging.Log(errorMessage);
            }

            return AccountActive;
        }

        private bool CostCentreIsActive(int ALedgerNumber, string ACostCentreCode = "")
        {
            bool CostCentreActive = false;
            bool CostCentreExists = true;

            //If empty, read value from combo
            if (ACostCentreCode.Length == 0)
            {
                if ((cmbDetailCostCentreCode.SelectedIndex != -1)
                    && (cmbDetailCostCentreCode.Count > 0)
                    && (cmbDetailCostCentreCode.GetSelectedString() != null))
                {
                    ACostCentreCode = cmbDetailCostCentreCode.GetSelectedString();
                }
                else
                {
                    return false;
                }
            }

            if (FCostCentreList == null)
            {
                SetupAccountCostCentreVariables(ALedgerNumber);
            }

            CostCentreActive = TFinanceControls.CostCentreIsActive(ALedgerNumber, ACostCentreCode, FCostCentreList, out CostCentreExists);

            if (!CostCentreExists && (ACostCentreCode.Length > 0))
            {
                string errorMessage = String.Format(Catalog.GetString("Cost Centre {0} does not exist in Ledger {1}!"),
                    ACostCentreCode,
                    ALedgerNumber);
                TLogging.Log(errorMessage);
            }

            return CostCentreActive;
        }

        private void ControlHasChanged(System.Object sender, EventArgs e)
        {
            bool NumericAmountChange = false;
            int ErrorCounter = FPetraUtilsObject.VerificationResultCollection.Count;

            if (sender.GetType() == typeof(TTxtCurrencyTextBox))
            {
                NumericAmountChange = true;
                CheckAmounts((TTxtCurrencyTextBox)sender);
            }

            ControlValidatedHandler(sender, e);

            //If no errors and amount has changed then update totals
            if (NumericAmountChange && (FPetraUtilsObject.VerificationResultCollection.Count == ErrorCounter))
            {
                UpdateTransactionTotals();
            }
        }

        private void CheckAmounts(TTxtCurrencyTextBox ATxtCurrencyTextBox)
        {
            bool debitChanged = (ATxtCurrencyTextBox.Name == "txtDebitAmount");

            if (!debitChanged && (ATxtCurrencyTextBox.Name != "txtCreditAmount"))
            {
                return;
            }

            if ((ATxtCurrencyTextBox.NumberValueDecimal == null) || !ATxtCurrencyTextBox.NumberValueDecimal.HasValue)
            {
                ATxtCurrencyTextBox.NumberValueDecimal = 0;
            }

            decimal valDebit = txtDebitAmount.NumberValueDecimal.Value;
            decimal valCredit = txtCreditAmount.NumberValueDecimal.Value;

            //If no changes then proceed no further
            if (debitChanged && (FDebitAmount == valDebit))
            {
                return;
            }
            else if (!debitChanged && (FCreditAmount == valCredit))
            {
                return;
            }

            if (debitChanged && ((valDebit > 0) && (valCredit > 0)))
            {
                txtCreditAmount.NumberValueDecimal = 0;
            }
            else if (!debitChanged && ((valDebit > 0) && (valCredit > 0)))
            {
                txtDebitAmount.NumberValueDecimal = 0;
            }
            else if (valDebit < 0)
            {
                txtDebitAmount.NumberValueDecimal = 0;
            }
            else if (valCredit < 0)
            {
                txtCreditAmount.NumberValueDecimal = 0;
            }

            //Reset class variables
            FDebitAmount = txtDebitAmount.NumberValueDecimal.Value;
            FCreditAmount = txtCreditAmount.NumberValueDecimal.Value;
        }

        /// <summary>
        /// enable or disable the buttons
        /// </summary>
        private void UpdateChangeableStatus()
        {
            bool changeable = !FPetraUtilsObject.DetailProtectedMode
                              && (SelectedBatchRow() != null)
                              && (FIsUnposted)
                              && (FJournalRow.JournalStatus == MFinanceConstants.BATCH_UNPOSTED);
            bool canDeleteAll = (FFilterAndFindObject.IsActiveFilterEqualToBase && !FContainsSystemGenerated);
            bool rowsInGrid = (grdDetails.Rows.Count > 1);

            // pnlDetailsProtected must be changed first: when the enabled property of the control is changed, the focus changes, which triggers validation
            pnlDetailsProtected = !changeable;
            pnlDetails.Enabled = (changeable && rowsInGrid);
            btnDeleteAll.Enabled = (changeable && canDeleteAll && rowsInGrid);
            pnlTransAnalysisAttributes.Enabled = changeable && (FPreviouslySelectedDetailRow == null || !FPreviouslySelectedDetailRow.SystemGenerated);
            //lblAnalAttributes.Enabled = (changeable && rowsInGrid);
            btnNew.Enabled = changeable;

            // If transaction is a reversal then we want to only have the Transaction date control enabled.
            // Only run this code if the journal contains at least one reversal transaction
            // or there are no reversals but controls are disabled (from viewing previous batch) and need enabled
            if (FContainsSystemGenerated || !cmbDetailCostCentreCode.Enabled)
            {
                foreach (Control cont in pnlDetails.Controls)
                {
                    if ((cont.Name != dtpDetailTransactionDate.Name) && (cont.Name != lblDetailTransactionDate.Name))
                    {
                        cont.Enabled = (FPreviouslySelectedDetailRow != null && !FPreviouslySelectedDetailRow.SystemGenerated)
                                       || (FPreviouslySelectedDetailRow == null && changeable);
                    }
                    else
                    {
                        cont.Enabled = changeable;
                    }
                }
            }
        }

        /// <summary>
        /// Delete transaction data from current GL Batch
        /// </summary>
        /// <param name="ABatchNumber"></param>
        /// <param name="AJournalNumber"></param>
        public void DeleteTransactionData(Int32 ABatchNumber, Int32 AJournalNumber = 0)
        {
            DataView TransAnalAttribDV = new DataView(FMainDS.ATransAnalAttrib);

            TransAnalAttribDV.RowFilter = String.Format("{0}={1} And " + (AJournalNumber > 0 ? "{2}={3}" : "{2}>{3}"),
                ATransAnalAttribTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransAnalAttribTable.GetJournalNumberDBName(),
                AJournalNumber);

            TransAnalAttribDV.Sort = String.Format("{0} DESC, {1} DESC, {2} DESC",
                ATransAnalAttribTable.GetJournalNumberDBName(),
                ATransAnalAttribTable.GetTransactionNumberDBName(),
                ATransAnalAttribTable.GetAnalysisTypeCodeDBName());

            foreach (DataRowView dr in TransAnalAttribDV)
            {
                dr.Delete();
            }

            DataView TransactionDV = new DataView(FMainDS.ATransaction);

            TransactionDV.RowFilter = String.Format("{0}={1} And " + (AJournalNumber > 0 ? "{2}={3}" : "{2}>{3}"),
                ATransactionTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransactionTable.GetJournalNumberDBName(),
                AJournalNumber);

            TransactionDV.Sort = String.Format("{0} DESC, {1} DESC",
                ATransactionTable.GetJournalNumberDBName(),
                ATransactionTable.GetTransactionNumberDBName());

            foreach (DataRowView dr in TransactionDV)
            {
                dr.Delete();
            }
        }

        private void DeleteAllTrans(System.Object sender, EventArgs e)
        {
            if ((FPreviouslySelectedDetailRow == null) || !FIsUnposted)
            {
                return;
            }
            else if (!FFilterAndFindObject.IsActiveFilterEqualToBase)
            {
                MessageBox.Show(Catalog.GetString("Please remove the filter before attempting to delete all transactions in this journal."),
                    Catalog.GetString("Delete All Transactions"));

                return;
            }

            if ((MessageBox.Show(String.Format(Catalog.GetString(
                             "You have chosen to delete all transactions in this Journal ({0}).\n\nDo you really want to continue?"),
                         FJournalNumber),
                     Catalog.GetString("Confirm Deletion"),
                     MessageBoxButtons.YesNo,
                     MessageBoxIcon.Question,
                     MessageBoxDefaultButton.Button2) != System.Windows.Forms.DialogResult.Yes))
            {
                return;
            }

            //Backup the Dataset for reversion purposes
            GLBatchTDS BackupMainDS = null;

            TFrmGLBatch FMyForm = (TFrmGLBatch) this.ParentForm;

            try
            {
                this.Cursor = Cursors.WaitCursor;
                //Specify current action
                FMyForm.FCurrentGLBatchAction = TGLBatchEnums.GLBatchAction.DELETINGALLTRANS;

                // Backup the Dataset for reversion purposes
                BackupMainDS = (GLBatchTDS)FMainDS.GetChangesTyped(false);

                // Unbind any transactions currently being edited in the Transaction Tab
                // but do not reset FBatchNumber to -1
                ClearCurrentSelection(0, false);

                //Delete transactions
                DataRow[] deleteTheseAttrib = FMainDS.ATransAnalAttrib.Select(String.Format("{0}={1} AND {2}={3}",
                        ATransAnalAttribTable.GetBatchNumberDBName(),
                        FBatchNumber,
                        ATransAnalAttribTable.GetJournalNumberDBName(),
                        FJournalNumber));

                foreach (DataRow row in deleteTheseAttrib)
                {
                    row.Delete();
                }

                DataRow[] deleteTheseTrans = FMainDS.ATransaction.Select(String.Format("{0}={1} AND {2}={3}",
                        ATransactionTable.GetBatchNumberDBName(),
                        FBatchNumber,
                        ATransactionTable.GetJournalNumberDBName(),
                        FJournalNumber));

                foreach (DataRow row in deleteTheseTrans)
                {
                    row.Delete();
                }

                //Set last journal number
                GetJournalRow().LastTransactionNumber = 0;

                FPetraUtilsObject.SetChangedFlag();

                // I Need to call save now, otherwise UpdateTransactionTotals will pull back the transactions I've just deleted!
                if (!FMyForm.SaveChangesManual(FMyForm.FCurrentGLBatchAction, true, false))
                {
                    FMyForm.GetBatchControl().UpdateUnpostedBatchDictionary();

                    MessageBox.Show(Catalog.GetString("The transactions were deleted but the changes could not be saved!"),
                        Catalog.GetString("Deletion Warning"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    SelectRowInGrid(1);
                }
                else
                {
                    UpdateChangeableStatus();
                    ClearControls();

                    MessageBox.Show(Catalog.GetString("All transactions have been deleted successfully!"),
                        Catalog.GetString("Success"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    FPetraUtilsObject.SetChangedFlag();
                    //Always update LastTransactionNumber first before updating totals
                    GLRoutines.UpdateJournalLastTransaction(FMainDS, FJournalRow);

                    //Update transaction totals and save the new figures
                    UpdateTransactionTotals();
                    FMyForm.SaveChanges();
                }

                FPreviouslySelectedDetailRow = null;
            }
            catch (Exception ex)
            {
                //Revert to previous state
                RevertDataSet(FMainDS, BackupMainDS, 1);

                TLogging.LogException(ex, Utilities.GetMethodSignature());
                throw;
            }
            finally
            {
                FMyForm.FCurrentGLBatchAction = TGLBatchEnums.GLBatchAction.NONE;
                this.Cursor = Cursors.Default;
            }
        }

        private bool PreDeleteManual(ATransactionRow ARowToDelete, ref string ADeletionQuestion)
        {
            bool AllowDeletion = true;

            if (FPreviouslySelectedDetailRow != null)
            {
                if (ARowToDelete.SystemGenerated)
                {
                    MessageBox.Show(string.Format(Catalog.GetString(
                                "Transaction {0} cannot be deleted as it is a system generated transaction"), ARowToDelete.TransactionNumber),
                        Catalog.GetString("Delete Transaction"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                ADeletionQuestion = String.Format(Catalog.GetString("Are you sure you want to delete transaction no. {0} from Journal {1}?"),
                    ARowToDelete.TransactionNumber,
                    ARowToDelete.JournalNumber);
            }

            return AllowDeletion;
        }

        /// <summary>
        /// Code to be run after the deletion process
        /// </summary>
        /// <param name="ARowToDelete">the row that was/was to be deleted</param>
        /// <param name="AAllowDeletion">whether or not the user was permitted to delete</param>
        /// <param name="ADeletionPerformed">whether or not the deletion was performed successfully</param>
        /// <param name="ACompletionMessage">if specified, is the deletion completion message</param>
        private void PostDeleteManual(ATransactionRow ARowToDelete,
            bool AAllowDeletion,
            bool ADeletionPerformed,
            string ACompletionMessage)
        {
            TFrmGLBatch FMyForm = (TFrmGLBatch) this.ParentForm;

            try
            {
                if (ADeletionPerformed)
                {
                    UpdateChangeableStatus();

                    if (!pnlDetails.Enabled)
                    {
                        ClearControls();
                    }

                    //Always update LastTransactionNumber first before updating totals
                    GLRoutines.UpdateJournalLastTransaction(FMainDS, FJournalRow);
                    UpdateTransactionTotals();

                    if (!FMyForm.SaveChangesManual(FMyForm.FCurrentGLBatchAction))
                    {
                        FMyForm.GetBatchControl().UpdateUnpostedBatchDictionary();

                        MessageBox.Show(Catalog.GetString("The transaction has been deleted but the changes are not saved!"),
                            Catalog.GetString("Deletion Warning"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    else
                    {
                        //message to user
                        MessageBox.Show(ACompletionMessage,
                            "Deletion Successful",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                else if (!AAllowDeletion && (ACompletionMessage.Length > 0))
                {
                    //message to user
                    MessageBox.Show(ACompletionMessage,
                        "Deletion not allowed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                else if (!ADeletionPerformed && (ACompletionMessage.Length > 0))
                {
                    //message to user
                    MessageBox.Show(ACompletionMessage,
                        "Deletion failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                FMyForm.FCurrentGLBatchAction = TGLBatchEnums.GLBatchAction.NONE;
            }
        }

        /// <summary>
        /// Deletes the current row and optionally populates a completion message
        /// </summary>
        /// <param name="ARowToDelete">the currently selected row to delete</param>
        /// <param name="ACompletionMessage">if specified, is the deletion completion message</param>
        /// <returns>true if row deletion is successful</returns>
        private bool DeleteRowManual(GLBatchTDSATransactionRow ARowToDelete, ref string ACompletionMessage)
        {
            bool DeletionSuccessful = false;

            ACompletionMessage = string.Empty;

            if (ARowToDelete == null)
            {
                return false;
            }

            //Check if row to delete is on server or not
            bool RowToDeleteIsNew = (ARowToDelete.RowState == DataRowState.Added);

            //Take a backup of FMainDS
            GLBatchTDS BackupMainDS = null;

            int TransactionNumberToDelete = ARowToDelete.TransactionNumber;
            int TopMostTransNo = FJournalRow.LastTransactionNumber;

            TFrmGLBatch FMyForm = (TFrmGLBatch) this.ParentForm;

            try
            {
                this.Cursor = Cursors.WaitCursor;
                //Specify current action
                FMyForm.FCurrentGLBatchAction = TGLBatchEnums.GLBatchAction.DELETINGTRANS;

                //Backup the Dataset for reversion purposes
                BackupMainDS = (GLBatchTDS)FMainDS.GetChangesTyped(false);

                if (RowToDeleteIsNew)
                {
                    ProcessNewlyAddedTransactionRowForDeletion(TransactionNumberToDelete);
                }
                else
                {
                    //Return modified row to last saved state to avoid validation failures
                    ARowToDelete.RejectChanges();
                    ShowDetails(ARowToDelete);

                    //Accept changes for other newly added rows, which by definition would have passed validation
                    if (OtherUncommittedRowsExist(FBatchNumber, FJournalNumber, TransactionNumberToDelete)
                        && !FMyForm.SaveChangesManual(FMyForm.FCurrentGLBatchAction))
                    {
                        FMyForm.GetBatchControl().UpdateUnpostedBatchDictionary();

                        MessageBox.Show(Catalog.GetString("The transaction has not been deleted!"),
                            Catalog.GetString("Deletion Warning"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return false;
                    }

                    // Make a copy of this journal for the server-side delete method.
                    GLBatchTDS TempDS = CopyTransDataToNewDataset(ARowToDelete.BatchNumber, ARowToDelete.JournalNumber);

                    //Clear the transactions and load newly saved dataset
                    RemoveTransactionsFromJournal(ARowToDelete.BatchNumber, ARowToDelete.JournalNumber);
                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.ProcessTransAndAttributesForDeletion(TempDS, FLedgerNumber, FBatchNumber,
                            FJournalNumber, TopMostTransNo, TransactionNumberToDelete));
                }

                FPreviouslySelectedDetailRow = null;
                FPetraUtilsObject.SetChangedFlag();

                ACompletionMessage = String.Format(Catalog.GetString("Transaction no.: {0} deleted successfully."),
                    TransactionNumberToDelete);

                DeletionSuccessful = true;
            }
            catch (Exception ex)
            {
                //Normally set in PostDeleteManual
                FMyForm.FCurrentGLBatchAction = TGLBatchEnums.GLBatchAction.NONE;

                //Revert to previous state
                RevertDataSet(FMainDS, BackupMainDS);

                TLogging.LogException(ex, Utilities.GetMethodSignature());
                throw;
            }
            finally
            {
                SetTransactionDefaultView();
                FFilterAndFindObject.ApplyFilter();
                this.Cursor = Cursors.Default;
            }

            return DeletionSuccessful;
        }

        /// <summary>
        ///
        /// </summary>
        public void InsertRow(System.Object sender, EventArgs e)
        {
            FPetraUtilsObject.VerificationResultCollection.Clear();

            if ((FPreviouslySelectedDetailRow == null)
                || (FPreviouslySelectedDetailRow.RowState == DataRowState.Deleted)
                || (FPreviouslySelectedDetailRow.RowState == DataRowState.Detached))
            {
                FPreviouslySelectedDetailRow = null;
                return;
            }

            Int32 batchNumber = FPreviouslySelectedDetailRow.BatchNumber;
            Int32 journalNumber = FPreviouslySelectedDetailRow.JournalNumber;
            Int32 insertBefore = FPreviouslySelectedDetailRow.TransactionNumber;

            DataView transactionView = new DataView(FMainDS.ATransaction);
            transactionView.RowFilter =
                "a_batch_number_i=" + batchNumber +
                " AND a_journal_number_i=" + journalNumber +
                " AND a_transaction_number_i>=" + insertBefore;

            // I can't simply renumber the rows that need to be "shifted down",
            // because that would cause constraints errors when the table is
            // committed. instead I'll delete the previous rows and create new ones.

            ATransactionTable modifiedTransactions = new ATransactionTable();

            foreach (DataRowView rv in transactionView)
            {
                ATransactionRow row = (ATransactionRow)rv.Row;
                ATransactionRow addedRow = modifiedTransactions.NewRowTyped();
                DataUtilities.CopyAllColumnValues(row, addedRow);
                addedRow.TransactionNumber += 1;
                modifiedTransactions.Rows.Add(addedRow);
                row.Delete();
            }

            FPreviouslySelectedDetailRow = null; // The selected row has been deleted, so I can't hold onto this.
            FMainDS.ATransaction.Merge(modifiedTransactions);

            DataView analysisView = new DataView(FMainDS.ATransAnalAttrib);
            analysisView.RowFilter =
                "a_batch_number_i=" + batchNumber +
                " AND a_journal_number_i=" + journalNumber +
                " AND a_transaction_number_i>=" + insertBefore;

            // I also need new analysis rows, otherwise when the server
            // deletes the transactions, the ATransAnalAttrib table will complain about foreign keys.
            ATransAnalAttribTable modifiedAnalysis = new ATransAnalAttribTable();

            foreach (DataRowView rv in analysisView)
            {
                ATransAnalAttribRow row = (ATransAnalAttribRow)rv.Row;
                ATransAnalAttribRow addedRow = modifiedAnalysis.NewRowTyped();
                DataUtilities.CopyAllColumnValues(row, addedRow);
                addedRow.TransactionNumber += 1;
                modifiedAnalysis.Rows.Add(addedRow);
                row.Delete();
            }

            FMainDS.ATransAnalAttrib.Merge(modifiedAnalysis);

            GLBatchTDSATransactionRow NewRow = FMainDS.ATransaction.NewRowTyped(true);
            NewRowManual(ref NewRow);

            //
            // The newly created row is inserted in the current position in the Journal.
            // There are no Analysis Attributes yet, so I don't need to worry about them.
            NewRow.TransactionNumber = insertBefore;
            FMainDS.ATransaction.Rows.Add(NewRow);

            ShowDetails(insertBefore);
            FPetraUtilsObject.SetChangedFlag();

            pnlTransAnalysisAttributes.Enabled = true;
            btnDeleteAll.Enabled = btnDelete.Enabled && (FFilterAndFindObject.IsActiveFilterEqualToBase) && !FContainsSystemGenerated;

            // I need to refresh Analysis Attributes options:
            AccountCodeDetailChanged(null, null);
        }

        private bool OtherUncommittedRowsExist(int ABatchNumber, int AJournalNumber, int ATransactionNumber)
        {
            bool UncommittedRowsExist = false;

            DataView TransDV = new DataView(FMainDS.ATransaction);
            DataView TransAnalDV = new DataView(FMainDS.ATransAnalAttrib);

            TransDV.RowFilter = String.Format("{0}={1} And {2}={3} And {4}<>{5}",
                ATransactionTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransactionTable.GetJournalNumberDBName(),
                AJournalNumber,
                ATransactionTable.GetTransactionNumberDBName(),
                ATransactionNumber);

            foreach (DataRowView drv in TransDV)
            {
                DataRow dr = (DataRow)drv.Row;

                if (dr.RowState == DataRowState.Added)
                {
                    UncommittedRowsExist = true;
                    break;
                }
            }

            if (!UncommittedRowsExist)
            {
                TransAnalDV.RowFilter = String.Format("{0}={1} And {2}={3} And {4}<>{5}",
                    ATransAnalAttribTable.GetBatchNumberDBName(),
                    ABatchNumber,
                    ATransAnalAttribTable.GetJournalNumberDBName(),
                    AJournalNumber,
                    ATransAnalAttribTable.GetTransactionNumberDBName(),
                    ATransactionNumber);

                foreach (DataRowView drv in TransAnalDV)
                {
                    DataRow dr = (DataRow)drv.Row;

                    if (dr.RowState == DataRowState.Added)
                    {
                        UncommittedRowsExist = true;
                        break;
                    }
                }
            }

            return UncommittedRowsExist;
        }

        //
        // Remove all the transactions from a Journal

        private void RemoveTransactionsFromJournal(Int32 ABatchNumber, Int32 AJournalNumber)
        {
            DataView transactionDV = new DataView(FMainDS.ATransaction);
            DataRowCollection transactionRows = FMainDS.ATransaction.Rows;
            DataView analysisDV = new DataView(FMainDS.ATransAnalAttrib);
            DataRowCollection analysisRows = FMainDS.ATransAnalAttrib.Rows;

            analysisDV.RowFilter = String.Format("{0}={1} AND {2}={3}",
                ATransAnalAttribTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransAnalAttribTable.GetJournalNumberDBName(),
                AJournalNumber);

            foreach (DataRowView drv in analysisDV)
            {
                DataRow dr = (DataRow)drv.Row;
                analysisRows.Remove(dr);
            }

            transactionDV.RowFilter = String.Format("{0}={1} AND {2}={3}",
                ATransactionTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransactionTable.GetJournalNumberDBName(),
                AJournalNumber);

            foreach (DataRowView drv in transactionDV)
            {
                DataRow dr = (DataRow)drv.Row;
                transactionRows.Remove(dr);
            }
        }

        private GLBatchTDS CopyTransDataToNewDataset(int ABatchNumber, int AJournalNumber)
        {
            GLBatchTDS TempDS = (GLBatchTDS)FMainDS.Copy();

            TempDS.Merge(FMainDS);

            DataView transactionsDV = new DataView(TempDS.ATransaction);
            DataView analysisDV = new DataView(TempDS.ATransAnalAttrib);

            analysisDV.RowFilter = String.Format("{0}<>{1} OR {2}<>{3}",
                ATransAnalAttribTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransAnalAttribTable.GetJournalNumberDBName(),
                AJournalNumber);

            foreach (DataRowView drv in analysisDV)
            {
                drv.Delete();
            }

            transactionsDV.RowFilter = String.Format("{0}<>{1} Or {2}<>{3}",
                ATransactionTable.GetBatchNumberDBName(),
                ABatchNumber,
                ATransactionTable.GetJournalNumberDBName(),
                AJournalNumber);

            foreach (DataRowView drv in transactionsDV)
            {
                drv.Delete();
            }

            TempDS.AcceptChanges();

            return TempDS;
        }

        private void RevertDataSet(GLBatchTDS AMainDS, GLBatchTDS ABackupDS, int ASelectRowInGrid = 0)
        {
            if ((ABackupDS != null) && (AMainDS != null))
            {
                AMainDS.RejectChanges();
                AMainDS.Merge(ABackupDS);

                if (ASelectRowInGrid > 0)
                {
                    SelectRowInGrid(ASelectRowInGrid);
                }
            }
        }

        private void ProcessNewlyAddedTransactionRowForDeletion(Int32 ATransactionNumber)
        {
            try
            {
                //Remove unaffected attributes and transactions
                DataView attributesDV = new DataView(FMainDS.ATransAnalAttrib);
                attributesDV.RowFilter = String.Format("{0}={1} AND {2}={3} AND {4}={5}",
                    ATransAnalAttribTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransAnalAttribTable.GetJournalNumberDBName(),
                    FJournalNumber,
                    ATransAnalAttribTable.GetTransactionNumberDBName(),
                    ATransactionNumber);

                foreach (DataRowView attrDRV in attributesDV)
                {
                    ATransAnalAttribRow attrRow = (ATransAnalAttribRow)attrDRV.Row;
                    attrRow.Delete();
                }

                DataView transactionsDV = new DataView(FMainDS.ATransaction);
                transactionsDV.RowFilter = String.Format("{0}={1} AND {2}={3} AND {4}={5}",
                    ATransactionTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    FJournalNumber,
                    ATransactionTable.GetTransactionNumberDBName(),
                    ATransactionNumber);

                foreach (DataRowView transDRV in transactionsDV)
                {
                    ATransactionRow tranRow = (ATransactionRow)transDRV.Row;
                    tranRow.Delete();
                }

                //Renumber the transactions and attributes in TempDS
                DataView attributesDV2 = new DataView(FMainDS.ATransAnalAttrib);
                attributesDV2.RowFilter = String.Format("{0}={1} AND {2}={3} AND {4}>{5}",
                    ATransAnalAttribTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransAnalAttribTable.GetJournalNumberDBName(),
                    FJournalNumber,
                    ATransAnalAttribTable.GetTransactionNumberDBName(),
                    ATransactionNumber);
                attributesDV2.Sort = String.Format("{0} ASC", ATransAnalAttribTable.GetTransactionNumberDBName());

                foreach (DataRowView attrDRV in attributesDV2)
                {
                    ATransAnalAttribRow attrRow = (ATransAnalAttribRow)attrDRV.Row;
                    attrRow.TransactionNumber--;
                }

                DataView transactionsDV2 = new DataView(FMainDS.ATransaction);
                transactionsDV2.RowFilter = String.Format("{0}={1} AND {2}={3} AND {4}>{5}",
                    ATransactionTable.GetBatchNumberDBName(),
                    FBatchNumber,
                    ATransactionTable.GetJournalNumberDBName(),
                    FJournalNumber,
                    ATransactionTable.GetTransactionNumberDBName(),
                    ATransactionNumber);
                transactionsDV2.Sort = String.Format("{0} ASC", ATransactionTable.GetTransactionNumberDBName());

                foreach (DataRowView transDRV in transactionsDV2)
                {
                    ATransactionRow tranRow = (ATransactionRow)transDRV.Row;
                    tranRow.TransactionNumber--;
                }
            }
            catch (Exception ex)
            {
                TLogging.LogException(ex, Utilities.GetMethodSignature());
                throw;
            }
        }

        /// <summary>
        /// clear the current selection
        /// </summary>
        /// <param name="ABatchToClear"></param>
        /// <param name="AResetFBatchNumber"></param>
        public void ClearCurrentSelection(int ABatchToClear = 0, bool AResetFBatchNumber = true)
        {
            if (this.FPreviouslySelectedDetailRow == null)
            {
                return;
            }
            else if ((ABatchToClear > 0) && (FPreviouslySelectedDetailRow.BatchNumber != ABatchToClear))
            {
                return;
            }

            //Set selection to null
            this.FPreviouslySelectedDetailRow = null;

            if (AResetFBatchNumber)
            {
                FBatchNumber = -1;
            }
        }

        private void ClearControls()
        {
            //Stop data change detection
            FPetraUtilsObject.DisableDataChangedEvent();

            //Clear combos
            cmbDetailAccountCode.SelectedIndex = -1;
            cmbDetailAccountCode.Text = string.Empty;
            cmbDetailCostCentreCode.SelectedIndex = -1;
            cmbDetailCostCentreCode.Text = string.Empty;
            cmbDetailKeyMinistryKey.SelectedIndex = -1;
            cmbDetailKeyMinistryKey.Text = string.Empty;
            //Clear Textboxes
            txtDetailNarrative.Clear();
            txtDetailReference.Clear();
            //Clear Numeric Textboxes
            txtDebitAmount.NumberValueDecimal = 0M;
            txtCreditAmount.NumberValueDecimal = 0M;
            txtDebitAmountBase.NumberValueDecimal = 0M;
            txtCreditAmountBase.NumberValueDecimal = 0M;
            //Refresh grids
            RefreshAnalysisAttributesGrid();
            //Enable data change detection
            FPetraUtilsObject.EnableDataChangedEvent();
        }

        /// <summary>
        /// if the cost centre code changes
        /// </summary>
        private void CostCentreCodeDetailChanged(object sender, EventArgs e)
        {
            if ((FLoadCompleted == false) || (FPreviouslySelectedDetailRow == null)
                || (cmbDetailCostCentreCode.GetSelectedString() == String.Empty)
                || (cmbDetailCostCentreCode.SelectedIndex == -1))
            {
                return;
            }

            // update key ministry combobox depending on account code and cost centre
            UpdateCmbDetailKeyMinistryKey();
        }

        /// <summary>
        /// if the account code changes, analysis types/attributes  have to be updated
        /// </summary>
        private void AccountCodeDetailChanged(object sender, EventArgs e)
        {
            if (FPreviouslySelectedDetailRow == null)
            {
                return;
            }

            if ((FPreviouslySelectedDetailRow.TransactionNumber == FTransactionNumber)
                && (FTransactionNumber != -1))
            {
                FAnalysisAttributesLogic.TransAnalAttrRequiredUpdating(FMainDS, FCacheDS,
                    cmbDetailAccountCode.GetSelectedString(), FTransactionNumber);
                RefreshAnalysisAttributesGrid();
            }

            // update key ministry combobox depending on account code and cost centre
            UpdateCmbDetailKeyMinistryKey();
        }

        /// <summary>
        /// if the cost centre code changes
        /// </summary>
        private void UpdateCmbDetailKeyMinistryKey()
        {
            Int64 RecipientKey = 0;

            // update key ministry combobox depending on account code and cost centre
            if ((cmbDetailAccountCode.GetSelectedString() == MFinanceConstants.FUND_TRANSFER_INCOME_ACC)
                && (cmbDetailCostCentreCode.GetSelectedString() != ""))
            {
                cmbDetailKeyMinistryKey.Enabled = true;
                TRemote.MFinance.Common.ServerLookups.WebConnectors.GetPartnerKeyForForeignCostCentreCode(FLedgerNumber,
                    cmbDetailCostCentreCode.GetSelectedString(),
                    out RecipientKey);
                TFinanceControls.GetRecipientData(ref cmbDetailKeyMinistryKey, RecipientKey);
                cmbDetailKeyMinistryKey.ComboBoxWidth = txtDetailNarrative.Width;
            }
            else
            {
                cmbDetailKeyMinistryKey.SetSelectedString("", -1);
                cmbDetailKeyMinistryKey.Enabled = false;
            }
        }

        private void ValidateDataDetailsManual(ATransactionRow ARow)
        {
            //Can be called from outside, so need to update fields
            FBatchRow = SelectedBatchRow();

            if (FBatchRow == null)
            {
                return;
            }

            FJournalRow = GetJournalRow();

            if (FJournalRow != null)
            {
                FJournalNumber = FJournalRow.JournalNumber;
                FJournalStatus = FJournalRow.JournalStatus;
            }

            FIsUnposted = (FBatchRow.BatchStatus == MFinanceConstants.BATCH_UNPOSTED);

            if ((ARow == null) || (FBatchRow.BatchNumber != ARow.BatchNumber) || !FIsUnposted)
            {
                return;
            }

            TVerificationResultCollection VerificationResultCollection = FPetraUtilsObject.VerificationResultCollection;

            Control controlToPass = null;

            //Local validation
            if (((txtDebitAmount.NumberValueDecimal.Value == 0)
                 && (txtCreditAmount.NumberValueDecimal.Value == 0))
                || (txtDebitAmount.NumberValueDecimal.Value < 0))
            {
                controlToPass = txtDebitAmount;
            }
            else if (txtCreditAmount.NumberValueDecimal.Value < 0)
            {
                controlToPass = txtCreditAmount;
            }
            else if (TSystemDefaults.GetStringDefault(SharedConstants.SYSDEFAULT_GLREFMANDATORY, "no") == "yes")
            {
                controlToPass = txtDetailReference;
            }
            else if ((VerificationResultCollection.Count == 1)
                     && (VerificationResultCollection[0].ResultCode == CommonErrorCodes.ERR_INVALIDNUMBER))
            {
                //The amount controls register as invalid even when value is correct. Need to reset
                //  Verifications accordingly.
                FPetraUtilsObject.VerificationResultCollection.Clear();
            }

            TSharedFinanceValidation_GL.ValidateGLDetailManual(this, FBatchRow, ARow, controlToPass, ref VerificationResultCollection,
                FValidationControlsDict);

            DataColumn ValidationColumn = ARow.Table.Columns[ATransactionTable.ColumnAccountCodeId];
            TScreenVerificationResult VerificationResult = null;

            if (!grdAnalAttributes.EndEdit(false))
            {
                VerificationResult = new TScreenVerificationResult(new TVerificationResult(this,
                        ErrorCodes.GetErrorInfo(PetraErrorCodes.ERR_INVALID_ANALYSIS_ATTRIBUTE_VALUE)),
                    ValidationColumn, grdAnalAttributes);
            }

            // Handle addition/removal to/from TVerificationResultCollection
            VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn, true);

            VerificationResult = null;

            if ((FPreviouslySelectedDetailRow != null)
                && !FAnalysisAttributesLogic.AccountAnalysisAttributeCountIsCorrect(
                    FPreviouslySelectedDetailRow.TransactionNumber,
                    FPreviouslySelectedDetailRow.AccountCode,
                    FMainDS,
                    FIsUnposted))
            {
                // This code is only running because of failure, so cause an error to occur.
                VerificationResult = new TScreenVerificationResult(new TVerificationResult(this,
                        ErrorCodes.GetErrorInfo(PetraErrorCodes.ERR_INCORRECT_ANALYSIS_ATTRIBUTE_COUNT,
                            new string[] { ARow.AccountCode, ARow.TransactionNumber.ToString() })),
                    ValidationColumn, grdAnalAttributes);
            }

            // Handle addition/removal to/from TVerificationResultCollection
            VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn, true);

            String ValueRequiredForType;
            VerificationResult = null;

            if ((FPreviouslySelectedDetailRow != null)
                && !FAnalysisAttributesLogic.AccountAnalysisAttributesValuesExist(
                    FPreviouslySelectedDetailRow.TransactionNumber,
                    FPreviouslySelectedDetailRow.AccountCode,
                    FMainDS,
                    out ValueRequiredForType,
                    FIsUnposted))
            {
                // This code is only running because of failure, so cause an error to occur.
                VerificationResult = new TScreenVerificationResult(new TVerificationResult(this,
                        ErrorCodes.GetErrorInfo(PetraErrorCodes.ERR_MISSING_ANALYSIS_ATTRIBUTE_VALUE,
                            new string[] { ValueRequiredForType, ARow.AccountCode, ARow.TransactionNumber.ToString() })),
                    ValidationColumn, grdAnalAttributes);
            }

            // Handle addition/removal to/from TVerificationResultCollection
            VerificationResultCollection.Auto_Add_Or_AddOrRemove(this, VerificationResult, ValidationColumn, true);
        }

        /// <summary>
        /// Set focus to the gid controltab
        /// </summary>
        public void FocusGrid()
        {
            if ((grdDetails != null) && grdDetails.CanFocus)
            {
                grdDetails.Focus();
            }
        }

        private void DebitAmountChanged(object sender, EventArgs e)
        {
            if (sender != null)
            {
                if ((txtDebitAmount.NumberValueDecimal != 0) && (txtCreditAmount.NumberValueDecimal != 0))
                {
                    txtCreditAmount.NumberValueDecimal = 0;
                }
            }
        }

        private void CreditAmountChanged(object sender, EventArgs e)
        {
            if (sender != null)
            {
                if ((txtCreditAmount.NumberValueDecimal != 0) && (txtDebitAmount.NumberValueDecimal != 0))
                {
                    txtDebitAmount.NumberValueDecimal = 0;
                }
            }
        }

        /// <summary>
        /// Shows the Filter/Find UserControl and switches to the Find Tab.
        /// </summary>
        public void ShowFindPanel()
        {
            if (FFilterAndFindObject.FilterFindPanel == null)
            {
                FFilterAndFindObject.ToggleFilter();
            }

            FFilterAndFindObject.FilterFindPanel.DisplayFindTab();
        }

        private void RunOnceOnParentActivationManual()
        {
            grdDetails.DataSource.ListChanged += new System.ComponentModel.ListChangedEventHandler(DataSource_ListChanged);

            if (TSystemDefaults.GetBooleanDefault(SharedConstants.SYSDEFAULT_GLREFMANDATORY, true) == false)
            {
                lblDetailReference.Text = lblDetailReference.Text.Replace("*", ":");
            }
        }

        private void DataSource_ListChanged(object sender, System.ComponentModel.ListChangedEventArgs e)
        {
            if (grdDetails.CanFocus && !FSuppressListChanged && (grdDetails.Rows.Count > 1))
            {
                grdDetails.AutoResizeGrid();

                // Once we have auto-sized once and there are more than 8 rows we don't auto-size any more (unless we load data again)
                FSuppressListChanged = (grdDetails.Rows.Count > 8);
            }

            // If the grid list changes we might need to disable the Delete All button
            btnDeleteAll.Enabled = btnDelete.Enabled && (FFilterAndFindObject.IsActiveFilterEqualToBase) && !FContainsSystemGenerated;
        }

        private void TransDateChanged(object sender, EventArgs e)
        {
            if ((FPetraUtilsObject == null) || FPetraUtilsObject.SuppressChangeDetection || (FPreviouslySelectedDetailRow == null))
            {
                return;
            }

            try
            {
                DateTime dateValue;

                string aDate = dtpDetailTransactionDate.Date.ToString();

                if (!DateTime.TryParse(aDate, out dateValue))
                {
                    dtpDetailTransactionDate.Date = SelectedBatchRow().DateEffective;
                }
            }
            catch
            {
                //Do nothing
            }
        }

        /// <summary>
        /// Cancel any changes made to this form
        /// </summary>
        public void CancelChangesToFixedBatches()
        {
            if ((SelectedBatchRow() != null) && (SelectedBatchRow().BatchStatus != MFinanceConstants.BATCH_UNPOSTED))
            {
                DataView transDV = new DataView(FMainDS.ATransaction);
                transDV.RowFilter = string.Format("{0}={1}",
                    ATransactionTable.GetBatchNumberDBName(),
                    SelectedBatchRow().BatchNumber);

                foreach (DataRowView drv in transDV)
                {
                    ATransactionRow tr = (ATransactionRow)drv.Row;
                    tr.RejectChanges();
                }
            }
        }

        private void ImportTransactionsFromFile(object sender, EventArgs e)
        {
            if (ValidateAllData(true, TErrorProcessingMode.Epm_All))
            {
                ((TFrmGLBatch)ParentForm).GetBatchControl().ImportTransactions(TUC_GLBatches_Import.TImportDataSourceEnum.FromFile);
                // The import method refreshes the screen if the import is successful
            }
        }

        private void ImportTransactionsFromClipboard(object sender, EventArgs e)
        {
            if (ValidateAllData(true, TErrorProcessingMode.Epm_All))
            {
                ((TFrmGLBatch)ParentForm).GetBatchControl().ImportTransactions(TUC_GLBatches_Import.TImportDataSourceEnum.FromClipboard);
                // The import method refreshes the screen if the import is successful
            }
        }

        /// <summary>
        /// Select a row in the grid
        ///   Called from outside
        /// </summary>
        /// <param name="ARowNumber"></param>
        public void SelectRow(int ARowNumber)
        {
            SelectRowInGrid(ARowNumber);
            UpdateRecordNumberDisplay();
        }

        private void FilterToggledManual(bool AFilterIsOff)
        {
            // The first time the filter is toggled on we need to set up the cost centre and account comboBoxes
            // This means showing inactive values in red
            // We achieve this by using our own owner draw mode event
            // Also the data source for the combos will be wrong because they have been cloned from items that may not have shown inactive values
            if ((AFilterIsOff == false) && !FDoneComboInitialise)
            {
                InitFilterFindAccountCodeComboBox((TCmbAutoComplete)FFilterAndFindObject.FilterPanelControls.FindControlByName("cmbDetailAccountCode"));
                InitFilterFindCostCentreComboBox((TCmbAutoComplete)FFilterAndFindObject.FilterPanelControls.FindControlByName(
                        "cmbDetailCostCentreCode"));
                InitFilterFindAccountCodeComboBox((TCmbAutoComplete)FFilterAndFindObject.FindPanelControls.FindControlByName("cmbDetailAccountCode"));
                InitFilterFindCostCentreComboBox((TCmbAutoComplete)FFilterAndFindObject.FindPanelControls.FindControlByName("cmbDetailCostCentreCode"));

                FDoneComboInitialise = true;
            }

            int prevMaxRows = grdDetails.MaxAutoSizeRows;
            grdDetails.MaxAutoSizeRows = 20;
            grdDetails.AutoResizeGrid();
            grdDetails.MaxAutoSizeRows = prevMaxRows;
        }

        /// <summary>
        /// Helper method that we can call to initialise each of the filter/find comboBoxes
        /// </summary>
        private void InitFilterFindAccountCodeComboBox(TCmbAutoComplete AFFInstance)
        {
            string rowFilter = TFinanceControls.PrepareAccountFilter(true, false, false, false, "");
            string sort = string.Format("{0}", AAccountTable.GetAccountCodeDBName());
            DataView dv = new DataView(TDataCache.TMFinance.GetCacheableFinanceTable(TCacheableFinanceTablesEnum.AccountList, FLedgerNumber),
                rowFilter, sort, DataViewRowState.CurrentRows);

            AFFInstance.DataSource = dv;
            AFFInstance.DrawMode = DrawMode.OwnerDrawFixed;
            AFFInstance.DrawItem += new DrawItemEventHandler(DrawComboBoxItem);
        }

        /// <summary>
        /// Helper method that we can call to initialise each of the filter/find comboBoxes
        /// </summary>
        private void InitFilterFindCostCentreComboBox(TCmbAutoComplete AFFInstance)
        {
            string rowFilter = TFinanceControls.PrepareCostCentreFilter(true, false, false, false);
            string sort = string.Format("{0}", ACostCentreTable.GetCostCentreCodeDBName());
            DataView dv = new DataView(TDataCache.TMFinance.GetCacheableFinanceTable(TCacheableFinanceTablesEnum.CostCentreList, FLedgerNumber),
                rowFilter, sort, DataViewRowState.CurrentRows);

            AFFInstance.DataSource = dv;
            AFFInstance.DrawMode = DrawMode.OwnerDrawFixed;
            AFFInstance.DrawItem += new DrawItemEventHandler(DrawComboBoxItem);
        }

        /// <summary>
        /// This method is called when the system wants to draw a comboBox item in the list.
        /// We choose the colour and weight for the font, showing inactive codes in bold red text
        /// </summary>
        private void DrawComboBoxItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            TCmbAutoComplete cmb = (TCmbAutoComplete)sender;
            DataRowView drv = (DataRowView)cmb.Items[e.Index];
            string content = drv[1].ToString();
            Brush brush;

            if (cmb.Name.StartsWith("cmbDetailCostCentre"))
            {
                brush = CostCentreIsActive(FLedgerNumber, content) ? Brushes.Black : Brushes.Red;
            }
            else if (cmb.Name.StartsWith("cmbDetailAccount"))
            {
                brush = AccountIsActive(FLedgerNumber, content) ? Brushes.Black : Brushes.Red;
            }
            else
            {
                throw new ArgumentException("Unexpected caller of DrawComboBoxItem event");
            }

            Font font = new Font(((Control)sender).Font, (brush == Brushes.Red) ? FontStyle.Bold : FontStyle.Regular);
            e.Graphics.DrawString(content, font, brush, new PointF(e.Bounds.X, e.Bounds.Y));
        }

        /// <summary>
        /// Select a special transaction number from outside
        /// </summary>
        /// <param name="ATransactionNumber"></param>
        /// <returns>True if the record is displayed in the grid, False otherwise</returns>
        public void SelectTransactionNumber(Int32 ATransactionNumber)
        {
            DataView myView = (grdDetails.DataSource as DevAge.ComponentModel.BoundDataView).DataView;

            for (int counter = 0; (counter < myView.Count); counter++)
            {
                int myViewTransactionNumber = (int)myView[counter]["a_transaction_number_i"];

                if (myViewTransactionNumber == ATransactionNumber)
                {
                    SelectRowInGrid(counter + 1);
                    break;
                }
            }
        }

        /// <summary>
        /// Confirm with the user concerning the presence of inactive fields
        ///  before saving changes to all changed batches
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AAction"></param>
        /// <param name="AJournalNumber"></param>
        /// <param name="ATransactionNumber"></param>
        /// <returns></returns>
        public bool AllowInactiveFieldValues(int ALedgerNumber,
            int ABatchNumber,
            TGLBatchEnums.GLBatchAction AAction,
            int AJournalNumber = 0,
            int ATransactionNumber = 0)
        {
            if (AAction == TGLBatchEnums.GLBatchAction.NONE)
            {
                AAction = TGLBatchEnums.GLBatchAction.SAVING;
            }

            TUC_GLBatches MainForm = ((TFrmGLBatch)ParentForm).GetBatchControl();

            bool InSaving = (AAction == TGLBatchEnums.GLBatchAction.SAVING);
            bool InPosting = false;
            bool InCancellingBatch = false;
            bool InCancellingJournal = false;
            bool InDeletingAllTrans = false;
            bool InDeletingTrans = false;
            bool InImporting = false;

            if (!InSaving)
            {
                switch (AAction)
                {
                    case TGLBatchEnums.GLBatchAction.TESTING:
                        InPosting = true;
                        break;

                    case TGLBatchEnums.GLBatchAction.POSTING:
                        InPosting = true;
                        break;

                    case TGLBatchEnums.GLBatchAction.CANCELLING:
                        InCancellingBatch = true;
                        break;

                    case TGLBatchEnums.GLBatchAction.CANCELLINGJOURNAL:
                        InCancellingJournal = true;
                        break;

                    case TGLBatchEnums.GLBatchAction.DELETINGALLTRANS:
                        InDeletingAllTrans = true;
                        break;

                    case TGLBatchEnums.GLBatchAction.DELETINGTRANS:
                        InDeletingTrans = true;
                        break;

                    case TGLBatchEnums.GLBatchAction.IMPORTING:
                        InImporting = true;
                        break;
                }
            }

            bool InDeletingData = (InCancellingJournal || InDeletingAllTrans || InDeletingTrans);

            bool WarnOfInactiveForPostingCurrentBatch = InPosting && MainForm.FInactiveValuesWarningOnGLPosting;

            //Variables for building warning message
            string WarningMessage = string.Empty;
            string WarningHeader = string.Empty;
            StringBuilder WarningList = new StringBuilder();

            //Find batches that have changed
            List <ABatchRow>BatchesToCheck = GetUnsavedBatchRowsList(ABatchNumber);
            List <int>BatchesWithInactiveValues = new List <int>();

            if (BatchesToCheck.Count > 0)
            {
                int currentBatchListNo;
                string batchNoList = string.Empty;

                int numInactiveFieldsPresent = 0;
                int numInactiveAccounts = 0;
                int numInactiveCostCentres = 0;
                int numInactiveAccountTypes = 0;
                int numInactiveAccountValues = 0;

                // AllowInactiveFieldValues can be called without LoadTransactions() being called in this class (e.g. select a Batch and Test or Post it
                // without visiting the Transactions tab. In that case, FLedgerNumber and FCacheDS have not been initialized. We need them later for
                // AnalysisCodeIsActive(), but we don't want to leave our copies lying around, because LoadTransactions() may not replace them if necessary.
                // Bug #5562
                bool InvalidateAnalysisCacheAfterUse = false;

                if (FCacheDS == null)
                {
                    InvalidateAnalysisCacheAfterUse = true;
                    FLedgerNumber = ALedgerNumber;
                    FCacheDS = TRemote.MFinance.GL.WebConnectors.LoadAAnalysisAttributes(ALedgerNumber, true);
                }

                foreach (ABatchRow gBR in BatchesToCheck)
                {
                    currentBatchListNo = gBR.BatchNumber;

                    bool checkingCurrentBatch = (currentBatchListNo == ABatchNumber);

                    //in a deleting process
                    bool noNeedToLoadDataForThisBatch = (InDeletingData && checkingCurrentBatch && (AJournalNumber > 0));

                    bool batchVerified = false;
                    bool batchExistsInDict = MainForm.FUnpostedBatchesVerifiedOnSavingDict.TryGetValue(currentBatchListNo, out batchVerified);

                    if (batchExistsInDict)
                    {
                        if (batchVerified && !(InPosting && checkingCurrentBatch && WarnOfInactiveForPostingCurrentBatch))
                        {
                            continue;
                        }
                    }
                    else if (!(InCancellingBatch && checkingCurrentBatch))
                    {
                        MainForm.FUnpostedBatchesVerifiedOnSavingDict.Add(currentBatchListNo, false);
                    }

                    //If processing batch about to be posted, only warn according to user preferences
                    if ((InPosting && checkingCurrentBatch && !WarnOfInactiveForPostingCurrentBatch)
                        || (InCancellingBatch && checkingCurrentBatch))
                    {
                        continue;
                    }

                    DataView journalDV = new DataView(FMainDS.AJournal);
                    DataView transDV = new DataView(FMainDS.ATransaction);
                    DataView attribDV = new DataView(FMainDS.ATransAnalAttrib);

                    //Make sure that journal and transaction data etc. is loaded for the current batch
                    journalDV.RowFilter = String.Format("{0}={1}",
                        AJournalTable.GetBatchNumberDBName(),
                        currentBatchListNo);

                    transDV.RowFilter = String.Format("{0}={1}",
                        ATransactionTable.GetBatchNumberDBName(),
                        currentBatchListNo);

                    attribDV.RowFilter = String.Format("{0}={1}",
                        ATransAnalAttribTable.GetBatchNumberDBName(),
                        currentBatchListNo);

                    if (!noNeedToLoadDataForThisBatch)
                    {
                        if (journalDV.Count == 0)
                        {
                            FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadAJournalAndRelatedTablesForBatch(ALedgerNumber,
                                    currentBatchListNo));

                            if (journalDV.Count == 0)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (transDV.Count == 0)
                            {
                                FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransactionAndRelatedTablesForBatch(ALedgerNumber,
                                        currentBatchListNo));

                                if (transDV.Count == 0)
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                if (attribDV.Count == 0)
                                {
                                    FMainDS.Merge(TRemote.MFinance.GL.WebConnectors.LoadATransAnalAttribForBatch(ALedgerNumber,
                                            currentBatchListNo));
                                }
                            }
                        }
                    }

                    //Check for inactive account or cost centre codes
                    transDV.Sort = String.Format("{0} ASC, {1} ASC",
                        ATransactionTable.GetJournalNumberDBName(),
                        ATransactionTable.GetTransactionNumberDBName());

                    foreach (DataRowView drv in transDV)
                    {
                        ATransactionRow transRow = (ATransactionRow)drv.Row;

                        //No need to record inactive values in transactions about to be deleted
                        if (checkingCurrentBatch
                            && ((InCancellingJournal && (AJournalNumber > 0) && (transRow.JournalNumber == AJournalNumber))
                                || (InDeletingTrans && (ATransactionNumber > 0) && (transRow.TransactionNumber == ATransactionNumber))))
                        {
                            continue;
                        }

                        if (!AccountIsActive(ALedgerNumber, transRow.AccountCode))
                        {
                            WarningList.AppendFormat(" Batch:{1} Journal:{2} Transaction:{3:00} has Account '{0}'{4}",
                                transRow.AccountCode,
                                transRow.BatchNumber,
                                transRow.JournalNumber,
                                transRow.TransactionNumber,
                                Environment.NewLine);

                            numInactiveAccounts++;

                            if (!BatchesWithInactiveValues.Contains(transRow.BatchNumber))
                            {
                                BatchesWithInactiveValues.Add(transRow.BatchNumber);
                            }
                        }

                        if (!CostCentreIsActive(ALedgerNumber, transRow.CostCentreCode))
                        {
                            WarningList.AppendFormat(" Batch:{1} Journal:{2} Transaction:{3:00} has Cost Centre '{0}'{4}",
                                transRow.CostCentreCode,
                                transRow.BatchNumber,
                                transRow.JournalNumber,
                                transRow.TransactionNumber,
                                Environment.NewLine);

                            numInactiveCostCentres++;

                            if (!BatchesWithInactiveValues.Contains(transRow.BatchNumber))
                            {
                                BatchesWithInactiveValues.Add(transRow.BatchNumber);
                            }
                        }
                    }

                    //Check anlysis attributes
                    attribDV.Sort = String.Format("{0} ASC, {1} ASC, {2} ASC",
                        ATransAnalAttribTable.GetJournalNumberDBName(),
                        ATransAnalAttribTable.GetTransactionNumberDBName(),
                        ATransAnalAttribTable.GetAnalysisTypeCodeDBName());

                    foreach (DataRowView drv2 in attribDV)
                    {
                        ATransAnalAttribRow analAttribRow = (ATransAnalAttribRow)drv2.Row;

                        //No need to record inactive values in transactions about to be deleted
                        if (checkingCurrentBatch
                            && ((InCancellingJournal && (AJournalNumber > 0) && (analAttribRow.JournalNumber == AJournalNumber))
                                || (InDeletingTrans && (ATransactionNumber > 0) && (analAttribRow.TransactionNumber == ATransactionNumber))))
                        {
                            continue;
                        }

                        if (!AnalysisCodeIsActive(analAttribRow.AccountCode, analAttribRow.AnalysisTypeCode))
                        {
                            WarningList.AppendFormat(" Batch:{1} Journal:{2} Transaction:{3:00} has Analysis Code '{0}'{4}",
                                analAttribRow.AnalysisTypeCode,
                                analAttribRow.BatchNumber,
                                analAttribRow.JournalNumber,
                                analAttribRow.TransactionNumber,
                                Environment.NewLine);

                            numInactiveAccountTypes++;

                            if (!BatchesWithInactiveValues.Contains(analAttribRow.BatchNumber))
                            {
                                BatchesWithInactiveValues.Add(analAttribRow.BatchNumber);
                            }
                        }

                        if (!AnalysisAttributeValueIsActive(analAttribRow.AnalysisTypeCode, analAttribRow.AnalysisAttributeValue))
                        {
                            WarningList.AppendFormat(" Batch:{1} Journal:{2} Transaction:{3:00} has Analysis Value '{0}'{4}",
                                analAttribRow.AnalysisAttributeValue,
                                analAttribRow.BatchNumber,
                                analAttribRow.JournalNumber,
                                analAttribRow.TransactionNumber,
                                Environment.NewLine);

                            numInactiveAccountValues++;

                            if (!BatchesWithInactiveValues.Contains(analAttribRow.BatchNumber))
                            {
                                BatchesWithInactiveValues.Add(analAttribRow.BatchNumber);
                            }
                        }
                    }
                }

                if (InvalidateAnalysisCacheAfterUse)
                {
                    FLedgerNumber = -1;
                    FCacheDS = null;
                }

                numInactiveFieldsPresent = (numInactiveAccounts + numInactiveCostCentres + numInactiveAccountTypes + numInactiveAccountValues);

                if (numInactiveFieldsPresent > 0)
                {
                    string batchList = string.Empty;
                    string otherChangedBatches = string.Empty;

                    BatchesWithInactiveValues.Sort();

                    //Update the dictionary
                    foreach (int batch in BatchesWithInactiveValues)
                    {
                        if (batch == ABatchNumber)
                        {
                            if ((!InPosting && (MainForm.FUnpostedBatchesVerifiedOnSavingDict[batch] == false))
                                || (InPosting && WarnOfInactiveForPostingCurrentBatch))
                            {
                                MainForm.FUnpostedBatchesVerifiedOnSavingDict[batch] = true;
                                batchList += (string.IsNullOrEmpty(batchList) ? "" : ", ") + batch.ToString();
                            }
                        }
                        else if (MainForm.FUnpostedBatchesVerifiedOnSavingDict[batch] == false)
                        {
                            MainForm.FUnpostedBatchesVerifiedOnSavingDict[batch] = true;
                            batchList += (string.IsNullOrEmpty(batchList) ? "" : ", ") + batch.ToString();
                            //Build a list of all batches except current batch
                            otherChangedBatches += (string.IsNullOrEmpty(otherChangedBatches) ? "" : ", ") + batch.ToString();
                        }
                    }

                    //Create header message
                    batchList = (otherChangedBatches.Length > 0 ? "es: " : ": ") + batchList;

                    if (!InImporting)
                    {
                        WarningHeader = "{0} inactive value(s) found in batch{1}{4}{4}Do you still want to continue with ";

                        if (InCancellingJournal)
                        {
                            WarningHeader += String.Format("cancelling journal {0} and saving changes to", AJournalNumber);
                        }
                        else if (InDeletingAllTrans)
                        {
                            WarningHeader += String.Format("deleting all transactions from journal {0} and saving changes to", AJournalNumber);
                        }
                        else if (InDeletingTrans)
                        {
                            WarningHeader += String.Format("deleting transaction {0} and saving changes to", ATransactionNumber);
                        }
                        else
                        {
                            WarningHeader += AAction.ToString().ToLower();
                        }

                        WarningHeader += " batch: {2}" + (otherChangedBatches.Length > 0 ? " and with saving: {3}" : "") + " ?{4}";

                        if (!InPosting || (otherChangedBatches.Length > 0))
                        {
                            WarningHeader += "{4}(You will only be warned once about inactive values when saving any batch!){4}";
                        }

                        WarningMessage = String.Format(Catalog.GetString(WarningHeader + "{4}Inactive values:{4}{5}{4}{6}{5}"),
                            numInactiveFieldsPresent,
                            batchList,
                            ABatchNumber,
                            otherChangedBatches,
                            Environment.NewLine,
                            new String('-', 80),
                            WarningList);
                    }
                    else
                    {
                        WarningHeader = "{1}{0}Warning: Inactive Account(s)or CostCentre(s){0}{1}{0}{0}";
                        WarningHeader += "Please note that {2} inactive value(s) were found in batch{3}{0}{0}";
                        WarningHeader += "{0}Inactive values:{0}{5}{0}{4}{5}{0}{0}";
                        WarningHeader += "These will need to be approved or changed before the batch can be posted.";
                        WarningMessage = String.Format(Catalog.GetString(WarningHeader),
                            Environment.NewLine,
                            new String('-', 80),
                            numInactiveFieldsPresent,
                            batchList,
                            WarningList,
                            new String('-', 80));
                    }

                    TFrmExtendedMessageBox extendedMessageBox = new TFrmExtendedMessageBox((TFrmGLBatch)ParentForm);

                    string header = string.Empty;

                    switch (AAction)
                    {
                        case TGLBatchEnums.GLBatchAction.POSTING:
                            header = "Post";
                            break;

                        case TGLBatchEnums.GLBatchAction.TESTING:
                            header = "Test-Post";
                            break;

                        case TGLBatchEnums.GLBatchAction.CANCELLING:
                            header = "Cancel";
                            break;

                        case TGLBatchEnums.GLBatchAction.CANCELLINGJOURNAL:
                            header = "Cancel Journal In";
                            break;

                        case TGLBatchEnums.GLBatchAction.DELETINGALLTRANS:
                            header = "Delete All Transaction Detail From";
                            break;

                        case TGLBatchEnums.GLBatchAction.DELETINGTRANS:
                            header = "Delete Transaction Detail From";
                            break;

                        case TGLBatchEnums.GLBatchAction.IMPORTING:
                            header = "Import";
                            break;

                        default:
                            header = "Save";
                            break;
                    }

                    header = Catalog.GetString(header + " GL Batch");

                    if (!InImporting)
                    {
                        return extendedMessageBox.ShowDialog(WarningMessage,
                            header, string.Empty,
                            TFrmExtendedMessageBox.TButtons.embbYesNo,
                            TFrmExtendedMessageBox.TIcon.embiQuestion) == TFrmExtendedMessageBox.TResult.embrYes;
                    }
                    else
                    {
                        return extendedMessageBox.ShowDialog(WarningMessage,
                            header, string.Empty,
                            TFrmExtendedMessageBox.TButtons.embbOK,
                            TFrmExtendedMessageBox.TIcon.embiWarning) == TFrmExtendedMessageBox.TResult.embrOK;
                    }
                }
            }

            return true;
        }

        private List <ABatchRow>GetUnsavedBatchRowsList(int ABatchToInclude = 0)
        {
            List <ABatchRow>RetVal = new List <ABatchRow>();
            List <int>BatchesWithChangesList = new List <int>();
            string BatchesWithChangesString = string.Empty;

            DataView BatchesDV = new DataView(FMainDS.ABatch);
            BatchesDV.RowFilter = String.Format("{0}='{1}'",
                ABatchTable.GetBatchStatusDBName(),
                MFinanceConstants.BATCH_UNPOSTED);
            BatchesDV.Sort = ABatchTable.GetBatchNumberDBName() + " ASC";

            DataView JournalDV = new DataView(FMainDS.AJournal);
            DataView TransDV = new DataView(FMainDS.ATransaction);
            DataView AttribDV = new DataView(FMainDS.ATransAnalAttrib);

            //Make sure that journal and transaction data etc. is loaded for the current batch
            JournalDV.Sort = String.Format("{0} ASC, {1} ASC",
                AJournalTable.GetBatchNumberDBName(),
                AJournalTable.GetJournalNumberDBName());

            TransDV.Sort = String.Format("{0} ASC, {1} ASC, {2} ASC",
                ATransactionTable.GetBatchNumberDBName(),
                ATransactionTable.GetJournalNumberDBName(),
                ATransactionTable.GetTransactionNumberDBName());

            AttribDV.Sort = String.Format("{0} ASC, {1} ASC, {2} ASC, {3} ASC",
                ATransAnalAttribTable.GetBatchNumberDBName(),
                ATransAnalAttribTable.GetJournalNumberDBName(),
                ATransAnalAttribTable.GetTransactionNumberDBName(),
                ATransAnalAttribTable.GetAnalysisTypeCodeDBName());

            //Add the batch number(s) of changed journals
            foreach (DataRowView dRV in JournalDV)
            {
                AJournalRow jR = (AJournalRow)dRV.Row;

                if (!BatchesWithChangesList.Contains(jR.BatchNumber)
                    && (jR.RowState != DataRowState.Unchanged))
                {
                    BatchesWithChangesList.Add(jR.BatchNumber);
                }
            }

            //Generate string of all batches found with changes
            if (BatchesWithChangesList.Count > 0)
            {
                BatchesWithChangesString = String.Join(",", BatchesWithChangesList);

                //Add any other batch number(s) of changed transactions
                TransDV.RowFilter = String.Format("{0} NOT IN ({1})",
                    ATransactionTable.GetBatchNumberDBName(),
                    BatchesWithChangesString);
            }

            foreach (DataRowView dRV in TransDV)
            {
                ATransactionRow tR = (ATransactionRow)dRV.Row;

                if (!BatchesWithChangesList.Contains(tR.BatchNumber)
                    && (tR.RowState != DataRowState.Unchanged))
                {
                    BatchesWithChangesList.Add(tR.BatchNumber);
                }
            }

            //Generate string of all batches found with changes
            if (BatchesWithChangesList.Count > 0)
            {
                BatchesWithChangesString = String.Join(",", BatchesWithChangesList);

                //Add any other batch number(s) of changed analysis attributes
                AttribDV.RowFilter = String.Format("{0} NOT IN ({1})",
                    ATransAnalAttribTable.GetBatchNumberDBName(),
                    BatchesWithChangesString);
            }

            foreach (DataRowView dRV in AttribDV)
            {
                ATransAnalAttribRow aR = (ATransAnalAttribRow)dRV.Row;

                if (!BatchesWithChangesList.Contains(aR.BatchNumber)
                    && (aR.RowState != DataRowState.Unchanged))
                {
                    BatchesWithChangesList.Add(aR.BatchNumber);
                }
            }

            BatchesWithChangesList.Sort();

            foreach (DataRowView dRV in BatchesDV)
            {
                ABatchRow batchRow = (ABatchRow)dRV.Row;

                if ((batchRow.BatchStatus == MFinanceConstants.BATCH_UNPOSTED)
                    && ((batchRow.BatchNumber == ABatchToInclude)
                        || BatchesWithChangesList.Contains(batchRow.BatchNumber)
                        || (batchRow.RowState != DataRowState.Unchanged)))
                {
                    RetVal.Add(batchRow);
                }
            }

            return RetVal;
        }

        private bool AnalysisCodeIsActive(String AAccountCode, String AAnalysisCode = "")
        {
            bool retVal = true;

            if ((AAnalysisCode == string.Empty) || (AAccountCode == string.Empty))
            {
                return retVal;
            }

            DataView dv = new DataView(FCacheDS.AAnalysisAttribute);

            dv.RowFilter = String.Format("{0}={1} AND {2}='{3}' AND {4}='{5}' AND {6}=true",
                AAnalysisAttributeTable.GetLedgerNumberDBName(),
                FLedgerNumber,
                AAnalysisAttributeTable.GetAccountCodeDBName(),
                AAccountCode,
                AAnalysisAttributeTable.GetAnalysisTypeCodeDBName(),
                AAnalysisCode,
                AAnalysisAttributeTable.GetActiveDBName());

            retVal = (dv.Count > 0);

            return retVal;
        }

        private bool AnalysisAttributeValueIsActive(String AAnalysisCode = "", String AAnalysisAttributeValue = "")
        {
            bool retVal = true;

            if ((AAnalysisCode == string.Empty) || (AAnalysisAttributeValue == string.Empty))
            {
                return retVal;
            }

            DataView dv = new DataView(FCacheDS.AFreeformAnalysis);

            dv.RowFilter = String.Format("{0}='{1}' AND {2}='{3}' AND {4}=true",
                AFreeformAnalysisTable.GetAnalysisTypeCodeDBName(),
                AAnalysisCode,
                AFreeformAnalysisTable.GetAnalysisValueDBName(),
                AAnalysisAttributeValue,
                AFreeformAnalysisTable.GetActiveDBName());

            retVal = (dv.Count > 0);

            return retVal;
        }

        #region BoundImage interface implementation

        /// <summary>
        /// Implementation of the interface member
        /// </summary>
        /// <param name="AContext">The context that identifies the column for which an image is to be evaluated</param>
        /// <param name="ADataRowView">The data containing the column of interest.  You will evaluate whether this column contains data that should have the image or not.</param>
        /// <returns>True if the image should be displayed in the current context</returns>
        public bool EvaluateBoundImage(BoundGridImage.AnnotationContextEnum AContext, DataRowView ADataRowView)
        {
            switch (AContext)
            {
                case BoundGridImage.AnnotationContextEnum.AccountCode:
                    ATransactionRow row = (ATransactionRow)ADataRowView.Row;
                    return !AccountIsActive(FLedgerNumber, row.AccountCode);

                case BoundGridImage.AnnotationContextEnum.CostCentreCode:
                    ATransactionRow row2 = (ATransactionRow)ADataRowView.Row;
                    return !CostCentreIsActive(FLedgerNumber, row2.CostCentreCode);

                case BoundGridImage.AnnotationContextEnum.AnalysisTypeCode:
                    ATransAnalAttribRow row3 = (ATransAnalAttribRow)ADataRowView.Row;
                    return !FAnalysisAttributesLogic.AnalysisCodeIsActive(cmbDetailAccountCode.GetSelectedString(),
                    FCacheDS.AAnalysisAttribute,
                    row3.AnalysisTypeCode);

                case BoundGridImage.AnnotationContextEnum.AnalysisAttributeValue:
                    ATransAnalAttribRow row4 = (ATransAnalAttribRow)ADataRowView.Row;
                    return !TAnalysisAttributes.AnalysisAttributeValueIsActive(ref FcmbAnalAttribValues,
                    FCacheDS.AFreeformAnalysis,
                    row4.AnalysisTypeCode,
                    row4.AnalysisAttributeValue);
            }

            return false;
        }

        #endregion
    }
}