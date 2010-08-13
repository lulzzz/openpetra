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
using System.Data;
using System.Collections.Specialized;
using Mono.Unix;
using Ict.Petra.Shared;
using Ict.Common;
using Ict.Common.DB;
using Ict.Common.Data;
using Ict.Common.Verification;
using Ict.Petra.Server.MFinance;
using Ict.Petra.Shared.MFinance;
using Ict.Petra.Shared.MFinance.Gift;
using Ict.Petra.Shared.MFinance.Gift.Data;
using Ict.Petra.Shared.MFinance.GL.Data;
using Ict.Petra.Shared.MFinance.Account.Data;
using Ict.Petra.Shared.MPartner.Partner.Data;
using Ict.Petra.Server.MFinance.Account.Data.Access;
using Ict.Petra.Server.MFinance.Gift.Data.Access;
using Ict.Petra.Server.MPartner.Partner.Data.Access;
using Ict.Petra.Server.MFinance.GL;
using Ict.Petra.Server.App.ClientDomain;

namespace Ict.Petra.Server.MFinance.Gift.WebConnectors
{
    ///<summary>
    /// This connector provides data for the finance Gift screens
    ///</summary>
    public class TTransactionWebConnector
    {
        /// <summary>
        /// create a new batch with a consecutive batch number in the ledger,
        /// and immediately store the batch and the new number in the database
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ADateEffective"></param>
        /// <returns></returns>
        public static GiftBatchTDS CreateAGiftBatch(Int32 ALedgerNumber, DateTime ADateEffective)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();
            ALedgerTable LedgerTable;
            TDBTransaction Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

            LedgerTable = ALedgerAccess.LoadByPrimaryKey(ALedgerNumber, Transaction);

            AGiftBatchRow NewRow = MainDS.AGiftBatch.NewRowTyped(true);
            NewRow.LedgerNumber = ALedgerNumber;
            LedgerTable[0].LastGiftBatchNumber++;
            NewRow.BatchNumber = LedgerTable[0].LastGiftBatchNumber;

            Int32 BatchYear, BatchPeriod;

            // if DateEffective is outside the range of open periods, use the most fitting date
            TFinancialYear.GetLedgerDatePostingPeriod(ALedgerNumber, ref ADateEffective, out BatchYear, out BatchPeriod, Transaction, true);
            NewRow.BatchYear = BatchYear;
            NewRow.BatchPeriod = BatchPeriod;
            NewRow.GlEffectiveDate = ADateEffective;
            NewRow.ExchangeRateToBase = 1.0;

            // TODO: bank account as a parameter, set on the gift matching screen, etc
            NewRow.BankAccountCode = DomainManager.GSystemDefaultsCache.GetStringDefault(
                SharedConstants.SYSDEFAULT_GIFTBANKACCOUNT + ALedgerNumber.ToString());

            if (NewRow.BankAccountCode.Length == 0)
            {
                // use the first bank account
                AAccountPropertyTable accountProperties = AAccountPropertyAccess.LoadViaALedger(ALedgerNumber, Transaction);
                accountProperties.DefaultView.RowFilter = AAccountPropertyTable.GetPropertyCodeDBName() + " = '" +
                                                          MFinanceConstants.ACCOUNT_PROPERTY_BANK_ACCOUNT + "' and " +
                                                          AAccountPropertyTable.GetPropertyValueDBName() + " = 'true'";

                if (accountProperties.DefaultView.Count > 0)
                {
                    NewRow.BankAccountCode = ((AAccountPropertyRow)accountProperties.DefaultView[0].Row).AccountCode;
                }
                else
                {
                    // needed for old Petra 2.x database
                    NewRow.BankAccountCode = "6000";
                }

                // TODO? DomainManager.GSystemDefaultsCache.SetDefault(SharedConstants.SYSDEFAULT_GIFTBANKACCOUNT + ALedgerNumber.ToString(), NewRow.BankAccountCode);
            }

            NewRow.BankCostCentre = Ict.Petra.Server.MFinance.GL.WebConnectors.TTransactionWebConnector.GetStandardCostCentre(ALedgerNumber);
            NewRow.CurrencyCode = LedgerTable[0].BaseCurrency;
            MainDS.AGiftBatch.Rows.Add(NewRow);

            TVerificationResultCollection VerificationResult;
            bool success = false;

            if (AGiftBatchAccess.SubmitChanges(MainDS.AGiftBatch, Transaction, out VerificationResult))
            {
                if (ALedgerAccess.SubmitChanges(LedgerTable, Transaction, out VerificationResult))
                {
                    success = true;
                }
            }

            if (success)
            {
                MainDS.AGiftBatch.AcceptChanges();
                DBAccess.GDBAccessObj.CommitTransaction();
                return MainDS;
            }
            else
            {
                DBAccess.GDBAccessObj.RollbackTransaction();
                throw new Exception("Error in CreateAGiftBatch");
            }
        }

        /// <summary>
        /// create a new batch with a consecutive batch number in the ledger,
        /// and immediately store the batch and the new number in the database
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        public static GiftBatchTDS CreateAGiftBatch(Int32 ALedgerNumber)
        {
            return CreateAGiftBatch(ALedgerNumber, DateTime.Today);
        }

        /// <summary>
        /// loads a list of batches for the given ledger
        /// also get the ledger for the base currency etc
        /// TODO: limit to period, limit to batch status, etc
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <returns></returns>
        public static GiftBatchTDS LoadAGiftBatch(Int32 ALedgerNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();
            TDBTransaction Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.ReadCommitted);

            ALedgerAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, Transaction);
            AGiftBatchAccess.LoadViaALedger(MainDS, ALedgerNumber, Transaction);
            DBAccess.GDBAccessObj.RollbackTransaction();
            return MainDS;
        }

        /// <summary>
        /// loads a list of gift transactions and details for the given ledger and batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <returns></returns>
        public static GiftBatchTDS LoadTransactions(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = new GiftBatchTDS();
            TDBTransaction Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.ReadCommitted);

            AGiftAccess.LoadViaAGiftBatch(MainDS, ALedgerNumber, ABatchNumber, Transaction);

            // AGiftDetailAccess.LoadViaGiftBatch does not exist; but we can easily simulate it:
            AGiftDetailAccess.LoadViaForeignKey(AGiftDetailTable.TableId,
                AGiftBatchTable.TableId,
                MainDS,
                new string[2] { AGiftBatchTable.GetLedgerNumberDBName(), AGiftBatchTable.GetBatchNumberDBName() },
                new System.Object[2] { ALedgerNumber, ABatchNumber },
                null,
                Transaction,
                null,
                0,
                0);

            DataView giftView = new DataView(MainDS.AGift);

            // fill the columns in the modified GiftDetail Table to show donorkey, dateentered etc in the grid
            foreach (GiftBatchTDSAGiftDetailRow giftDetail in MainDS.AGiftDetail.Rows)
            {
                // get the gift
                giftView.RowFilter = AGiftTable.GetGiftTransactionNumberDBName() + " = " + giftDetail.GiftTransactionNumber.ToString();

                AGiftRow giftRow = (AGiftRow)giftView[0].Row;

                StringCollection shortName = new StringCollection();
                shortName.Add(PPartnerTable.GetPartnerShortNameDBName());
                PPartnerTable partner = PPartnerAccess.LoadByPrimaryKey(giftRow.DonorKey, shortName, Transaction);

                giftDetail.DonorKey = giftRow.DonorKey;
                giftDetail.DonorName = partner[0].PartnerShortName;
                giftDetail.DateEntered = giftRow.DateEntered;
            }

            DBAccess.GDBAccessObj.RollbackTransaction();

            return MainDS;
        }

        /// <summary>
        /// this will store all new and modified batches, gift transactions and details
        /// </summary>
        /// <param name="AInspectDS"></param>
        /// <param name="AVerificationResult"></param>
        /// <returns></returns>
        public static TSubmitChangesResult SaveGiftBatchTDS(ref GiftBatchTDS AInspectDS,
            out TVerificationResultCollection AVerificationResult)
        {
            TSubmitChangesResult SubmissionResult = TSubmitChangesResult.scrError;

            SubmissionResult = GiftBatchTDSAccess.SubmitChanges(AInspectDS, out AVerificationResult);

            if (SubmissionResult == TSubmitChangesResult.scrOK)
            {
                // TODO: check that gifts are in consecutive numbers?
                // TODO: check that gift details are in consecutive numbers, no gift without gift details?
                // Problem: unchanged rows will not arrive here? check after committing, and update the gift batch again
                // TODO: calculate hash of saved batch or batch of saved gift
            }

            return SubmissionResult;
        }

        /// <summary>
        /// creates the GL batch needed for posting the gift batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="AGiftDataset"></param>
        /// <returns></returns>
        private static GLBatchTDS CreateGLBatchAndTransactionsForPostingGifts(Int32 ALedgerNumber, ref GiftBatchTDS AGiftDataset)
        {
            // create one GL batch
            GLBatchTDS GLDataset = Ict.Petra.Server.MFinance.GL.WebConnectors.TTransactionWebConnector.CreateABatch(ALedgerNumber);

            ABatchRow batch = GLDataset.ABatch[0];

            AGiftBatchRow giftbatch = AGiftDataset.AGiftBatch[0];

            batch.BatchDescription = Catalog.GetString("Gift Batch " + giftbatch.BatchNumber.ToString());
            batch.DateEffective = giftbatch.GlEffectiveDate;

            // TODO batchperiod depending on date effective; or fix that when posting?
            // batch.BatchPeriod =
            batch.BatchStatus = MFinanceConstants.BATCH_UNPOSTED;

            // one gift batch only has one currency, create only one journal
            AJournalRow journal = GLDataset.AJournal.NewRowTyped();
            journal.LedgerNumber = batch.LedgerNumber;
            journal.BatchNumber = batch.BatchNumber;
            journal.JournalNumber = 1;
            journal.DateEffective = batch.DateEffective;
            journal.JournalPeriod = giftbatch.BatchPeriod;
            journal.TransactionCurrency = giftbatch.CurrencyCode;
            journal.JournalDescription = batch.BatchDescription;
            journal.TransactionTypeCode = MFinanceConstants.TRANSACTION_GIFT;
            journal.SubSystemCode = MFinanceConstants.SUB_SYSTEM_GR;
            journal.LastTransactionNumber = 0;
            journal.DateOfEntry = DateTime.Now;

            // TODO journal.ExchangeRateToBase and journal.ExchangeRateTime
            journal.ExchangeRateToBase = 1.0;
            GLDataset.AJournal.Rows.Add(journal);

            foreach (GiftBatchTDSAGiftDetailRow giftdetail in AGiftDataset.AGiftDetail.Rows)
            {
                ATransactionRow transaction = null;

                // do we have already a transaction for this costcentre&account?
                GLDataset.ATransaction.DefaultView.RowFilter = String.Format("{0}='{1}' and {2}='{3}'",
                    ATransactionTable.GetAccountCodeDBName(),
                    giftdetail.AccountCode,
                    ATransactionTable.GetCostCentreCodeDBName(),
                    giftdetail.CostCentreCode);

                if (GLDataset.ATransaction.DefaultView.Count == 0)
                {
                    transaction = GLDataset.ATransaction.NewRowTyped();
                    transaction.LedgerNumber = journal.LedgerNumber;
                    transaction.BatchNumber = journal.BatchNumber;
                    transaction.JournalNumber = journal.JournalNumber;
                    transaction.TransactionNumber = ++journal.LastTransactionNumber;
                    transaction.AccountCode = giftdetail.AccountCode;
                    transaction.CostCentreCode = giftdetail.CostCentreCode;
                    transaction.Narrative = "GB - Gift Batch " + giftbatch.BatchNumber.ToString();
                    transaction.Reference = "GB" + giftbatch.BatchNumber.ToString();
                    transaction.DebitCreditIndicator = false;
                    transaction.TransactionAmount = 0;
                    transaction.AmountInBaseCurrency = 0;
                    transaction.TransactionDate = giftbatch.GlEffectiveDate;

                    GLDataset.ATransaction.Rows.Add(transaction);
                }
                else
                {
                    transaction = (ATransactionRow)GLDataset.ATransaction.DefaultView[0].Row;
                }

                transaction.TransactionAmount += giftdetail.GiftTransactionAmount;
                transaction.AmountInBaseCurrency += giftdetail.GiftAmount;

                // TODO: for other currencies a post to a_ledger.a_forex_gains_losses_account_c ???

                // TODO: do the fee calculation, a_fees_payable, a_fees_receivable
            }

            ATransactionRow transactionForTotals = GLDataset.ATransaction.NewRowTyped();
            transactionForTotals.LedgerNumber = journal.LedgerNumber;
            transactionForTotals.BatchNumber = journal.BatchNumber;
            transactionForTotals.JournalNumber = journal.JournalNumber;
            transactionForTotals.TransactionNumber = ++journal.LastTransactionNumber;
            transactionForTotals.TransactionAmount = 0;
            transactionForTotals.TransactionDate = giftbatch.GlEffectiveDate;

            foreach (GiftBatchTDSAGiftDetailRow giftdetail in AGiftDataset.AGiftDetail.Rows)
            {
                transactionForTotals.TransactionAmount += giftdetail.GiftTransactionAmount;
            }

            transactionForTotals.DebitCreditIndicator = true;

            // TODO: support foreign currencies
            transactionForTotals.AmountInBaseCurrency = transactionForTotals.TransactionAmount;

            // TODO: account and costcentre based on linked costcentre, current commitment, and Motivation detail
            // if motivation cost centre is a summary cost centre, make sure the transaction costcentre is reporting to that summary cost centre
            // Careful: modify gift cost centre and account and recipient field only when the amount is positive.
            // adjustments and reversals must remain on the original value
            transactionForTotals.AccountCode = giftbatch.BankAccountCode;
            transactionForTotals.CostCentreCode = Ict.Petra.Server.MFinance.GL.WebConnectors.TTransactionWebConnector.GetStandardCostCentre(
                ALedgerNumber);
            transactionForTotals.Narrative = "Deposit from receipts - Gift Batch " + giftbatch.BatchNumber.ToString();
            transactionForTotals.Reference = "GB" + giftbatch.BatchNumber.ToString();

            GLDataset.ATransaction.Rows.Add(transactionForTotals);

            GLDataset.ATransaction.DefaultView.RowFilter = string.Empty;

            foreach (ATransactionRow transaction in GLDataset.ATransaction.Rows)
            {
                if (transaction.DebitCreditIndicator)
                {
                    journal.JournalDebitTotal += transaction.TransactionAmount;
                    batch.BatchDebitTotal += transaction.TransactionAmount;
                }
                else
                {
                    journal.JournalCreditTotal += transaction.TransactionAmount;
                    batch.BatchCreditTotal += transaction.TransactionAmount;
                }
            }

            batch.LastJournal = 1;

            return GLDataset;
        }

        /// create GiftBatchTDS with the gift batch to post, and all gift transactions and details, and motivation details
        private static GiftBatchTDS LoadGiftBatchForPosting(Int32 ALedgerNumber, Int32 ABatchNumber)
        {
            GiftBatchTDS MainDS = LoadTransactions(ALedgerNumber, ABatchNumber);

            TDBTransaction Transaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

            AGiftBatchAccess.LoadByPrimaryKey(MainDS, ALedgerNumber, ABatchNumber, Transaction);
            AMotivationDetailAccess.LoadViaALedger(MainDS, ALedgerNumber, Transaction);

            DBAccess.GDBAccessObj.RollbackTransaction();

            return MainDS;
        }

        /// <summary>
        /// post a Gift Batch
        /// </summary>
        /// <param name="ALedgerNumber"></param>
        /// <param name="ABatchNumber"></param>
        /// <param name="AVerifications"></param>
        public static bool PostGiftBatch(Int32 ALedgerNumber, Int32 ABatchNumber, out TVerificationResultCollection AVerifications)
        {
            AVerifications = null;
            bool ResultValue = false;

            GiftBatchTDS MainDS = LoadGiftBatchForPosting(ALedgerNumber, ABatchNumber);

            foreach (GiftBatchTDSAGiftDetailRow giftDetail in MainDS.AGiftDetail.Rows)
            {
                // find motivation detail
                AMotivationDetailRow motivationRow =
                    (AMotivationDetailRow)MainDS.AMotivationDetail.Rows.Find(new object[] { ALedgerNumber, giftDetail.MotivationGroupCode,
                                                                                            giftDetail.MotivationDetailCode });

                // TODO: make sure the correct costcentres and accounts are used (check pm_staff_data for commitment period, and motivation details, etc)
                // set custom column giftdetail.AccountCode motivation
                giftDetail.AccountCode = motivationRow.AccountCode;

                // TODO deal with different currencies; at the moment assuming base currency
                giftDetail.GiftAmount = giftDetail.GiftTransactionAmount;
            }

            // TODO if already posted, fail
            MainDS.AGiftBatch[0].BatchStatus = MFinanceConstants.BATCH_POSTED;

            TDBTransaction SubmitChangesTransaction = null;

            try
            {
                // create GL batch
                GLBatchTDS GLDataset = CreateGLBatchAndTransactionsForPostingGifts(ALedgerNumber, ref MainDS);

                ABatchRow batch = GLDataset.ABatch[0];

                // save the batch
                if (Ict.Petra.Server.MFinance.GL.WebConnectors.TTransactionWebConnector.SaveGLBatchTDS(ref GLDataset,
                        out AVerifications) == TSubmitChangesResult.scrOK)
                {
                    // post the batch
                    if (!Ict.Petra.Server.MFinance.GL.TGLPosting.PostGLBatch(ALedgerNumber, batch.BatchNumber,
                            out AVerifications))
                    {
                        // TODO: what if posting fails? do we have an orphaned GL batch lying around? can this be put into one single transaction? probably not
                        // TODO: we should cancel that GL batch
                    }
                    else
                    {
                        SubmitChangesTransaction = DBAccess.GDBAccessObj.BeginTransaction(IsolationLevel.Serializable);

                        // store GiftBatch and GiftDetails to database
                        if (AGiftBatchAccess.SubmitChanges(MainDS.AGiftBatch, SubmitChangesTransaction,
                                out AVerifications))
                        {
                            if (AGiftAccess.SubmitChanges(MainDS.AGift, SubmitChangesTransaction,
                                    out AVerifications))
                            {
                                // save changed motivation details, costcentre etc to database
                                if (AGiftDetailAccess.SubmitChanges(MainDS.AGiftDetail, SubmitChangesTransaction, out AVerifications))
                                {
                                    ResultValue = true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // we should not get here; how would the database get broken?
                // TODO do we need a bigger transaction around everything?

                TLogging.Log("after submitchanges: exception " + e.Message);

                if (SubmitChangesTransaction != null)
                {
                    DBAccess.GDBAccessObj.RollbackTransaction();
                }

                throw new Exception(e.ToString() + " " + e.Message);
            }

            if (ResultValue)
            {
                DBAccess.GDBAccessObj.CommitTransaction();
            }
            else
            {
                DBAccess.GDBAccessObj.RollbackTransaction();
            }

            return ResultValue;
        }
    }
}