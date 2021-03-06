﻿//
// DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
//
// @Authors:
//       berndr
//
// Copyright 2004-2011 by OM International
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
using System.Windows.Forms;
using GNU.Gettext;
using Ict.Common;
using Ict.Petra.Shared.MConference;

namespace Ict.Petra.Client.MReporting.Gui.MConference
{
    /// <summary>
    /// Description of TFrmChargedFieldReports.ManualCode.
    /// </summary>
    public class TFrmChargedFieldReport : TFrmFieldReports
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="AParentForm"></param>
        /// <param name="ASecurityContext"></param>
        public TFrmChargedFieldReport(Form AParentForm, string ASecurityContext = "") : base(AParentForm, ASecurityContext)
        {
            this.Text = Catalog.GetString("Charged Field Report");

            SetReportParameters("Conference\\\\chargedfieldreport.xml,Conference\\\\conference.xml",
                "Charged Field Report");

            grdFields_InitialiseData(TUnitTypeEnum.utChargedFields);
        }
    }
}