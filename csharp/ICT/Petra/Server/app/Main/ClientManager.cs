﻿/*************************************************************************
 *
 * DO NOT REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
 *
 * @Authors:
 *       christiank
 *
 * Copyright 2004-2009 by OM International
 *
 * This file is part of OpenPetra.org.
 *
 * OpenPetra.org is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * OpenPetra.org is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with OpenPetra.org.  If not, see <http://www.gnu.org/licenses/>.
 *
 ************************************************************************/
using System;
using System.Collections;
using System.Configuration;
using System.Data;
using System.Reflection;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Lifetime;
using System.Security.Principal;
using System.Resources;
using Mono.Unix;
using Ict.Common;
using Ict.Common.DB;
using Ict.Common.Verification;
using Ict.Petra.Shared;
using Ict.Petra.Shared.Interfaces;
using Ict.Petra.Shared.Security;
using Ict.Petra.Shared.RemotedExceptions;
using Ict.Petra.Shared.MSysMan;
using Ict.Petra.Server.App.Core;
using Ict.Petra.Server.App.ClientDomain;
using Ict.Petra.Server.App.Core.Security;
using Ict.Petra.Server.App.Main;
using System.Threading;
using Ict.Petra.Server.MSysMan;
using Ict.Petra.Server.MSysMan.Maintenance;
using Ict.Petra.Server.MSysMan.Security;

namespace Ict.Petra.Server.App.Main
{
    /// <summary>
    /// several states for app domains
    /// </summary>
    public enum TAppDomainStatus
    {
        /// <summary>
        /// during login verification
        /// </summary>
        adsConnectingLoginVerification,

        /// <summary>
        /// login was successful
        /// </summary>
        adsConnectingLoginOK,

        /// <summary>
        /// app domain has been setup
        /// </summary>
        adsConnectingAppDomainSetupOK,

        /// <summary>
        /// app domain is active
        /// </summary>
        adsActive,

        /// <summary>
        /// app domain is not busy
        /// </summary>
        adsIdle,

        /// <summary>
        /// the db connection is being closed
        /// </summary>
        adsDisconnectingDBClosing,

        /// <summary>
        /// app domain is shutting down
        /// </summary>
        adsDisconnectingAppDomainUnloading,

        /// <summary>
        /// app domain has been stopped
        /// </summary>
        adsStopped
    };

    /// <summary>
    /// some global variables
    /// </summary>
    class ClientManager
    {
        public static System.Object UAppDomainUnloadMonitor;

        public static void InitializeUnit()
        {
            TCacheableTablesManager.InitializeUnit();
            TClientManager.UCacheableTablesManager = new TCacheableTablesManager(new TDelegateSendClientTask(TClientManager.QueueClientTask));
        }
    }

    /// <summary>
    /// Main class for Client connection and disconnection and other Client actions.
    ///
    /// For each Client that connects a separate AppDomain is created, into which
    /// one DLL for the management of the Client AppDomain is loaded, plus a DLL for
    /// each Petra Module.
    /// From then on all Client calls go only into the DLL's in the Client AppDomain,
    /// except for notifying the Server that the Client wants to disconnect.
    ///
    /// For each active Client connection an TRunningAppDomain entry in a SortedList
    /// is maintained. This SortedList can be iterated to find out all currently
    /// active Client connections or details about a connected Client.
    ///
    /// TClientManager gets remoted and can be accessed via an Interface from a
    /// Client application such as PetraClient_Experimenting.exe
    ///
    /// TClientManager is also used by TServerManager to perform actions on connected
    /// Clients and to request information about Clients.
    ///
    /// </summary>
    public class TClientManager : MarshalByRefObject, IClientManagerInterface
    {
        /// <summary>
        /// static error message
        /// </summary>
        public const String StrClientServerExeProgramVersionMismatchMessage = "The Program Version of " +
                                                                              "your Petra Client ({0}) does not match the Program Version of the Petra "
                                                                              +
                                                                              "Server ({1}).\r\nA Petra Client cannot connect to a Petra " +
                                                                              "Server unless the Program Versions match. You need to install a Petra "
                                                                              +
                                                                              "Client with the correct Program Version.";

        /// <summary>Holds reference to an instance of TClientManager (for calls inside a static function)</summary>
        private static TClientManager UClientManagerObj;

        /// <summary>Holds reference to an instance of TSystemDefaultsCache (for System Defaults lookups)</summary>
        private static TSystemDefaultsCache USystemDefaultsCache;

        /// <summary>Holds reference to an instance of TCacheableTablesManager (for caching of DataTables)</summary>
        public static TCacheableTablesManager UCacheableTablesManager;

        /// <summary>Used for ThreadLocking a critical part of the Client Connection code to make sure that this code is executed by exactly one Client at any given time</summary>
        private static System.Object UConnectClientMonitor;

        /// Holds a TRunningAppDomain object for each Client that is currently connected to the Petra Server. IMPORTANT: to access this SortedList in a threadsave manner using an IDictionaryEnumerator, it is important that this is done only within a
        /// <summary>block of code that is encapsulated using Monitor.TryEnter(UClientObjects.SyncRoot)!!!</summary>
        private static SortedList UClientObjects;

        private DateTime FStartTime;

        /// <summary>Holds a reference to the ClientManagerCallForwarder Object.</summary>
        private static TClientManagerCallForwarder FClientManagerCallForwarder;

        /// <summary>Holds the total number of Clients that connected to the Petra Server since the start of the Petra Server.</summary>
        private static System.Int32 FClientsConnectedTotal;

        /// <summary>
        /// Called by TClientManager to request the number of Clients that are currently
        /// connected to the Petra Server.
        ///
        /// </summary>
        public static System.Int32 ClientsConnected
        {
            get
            {
                System.Int32 ReturnValue;
                Int16 ClientCounter;
                IDictionaryEnumerator ClientEnum;

                if (UClientObjects != null)
                {
                    ClientCounter = 0;
                    ClientEnum = UClientObjects.GetEnumerator();
                    try
                    {
                        if (Monitor.TryEnter(UClientObjects.SyncRoot))
                        {
                            // Iterate over all Clients and count the ones that are currently connected
                            while (ClientEnum.MoveNext())
                            {
                                TRunningAppDomain app = ((TRunningAppDomain)ClientEnum.Value);

                                if ((app.FAppDomainStatus == TAppDomainStatus.adsActive) || (app.FAppDomainStatus == TAppDomainStatus.adsIdle))
                                {
                                    ClientCounter++;
                                }
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(UClientObjects.SyncRoot);
                    }
                    ReturnValue = ClientCounter;
                }
                else
                {
                    ReturnValue = 0;
                }

                return ReturnValue;
            }
        }

        /// <summary>
        /// Called by TClientManager to request the total number of Clients that
        /// connected to the Petra Server since the start of the Petra Server.
        ///
        /// </summary>
        public static System.Int32 ClientsConnectedTotal
        {
            get
            {
                return FClientsConnectedTotal;
            }
        }

        /// <summary>
        /// Used by ServerManager to pass in a reference to the TSystemDefaultsCache
        /// object. This reference is passed in turn by ClientManager into each
        /// ClientDomain to give each ClientDomain access to the System Defaults cache.
        ///
        /// </summary>
        public static TSystemDefaultsCache SystemDefaultsCache
        {
            get
            {
                return USystemDefaultsCache;
            }

            set
            {
                USystemDefaultsCache = value;
            }
        }


        /// <summary>
        /// Called by TClientDomainManager to queue a ClientTask for a certain Client.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client; use -1 to queue the
        /// ClientTask to all Clients</param>
        /// <param name="ATaskGroup">Group of the Task</param>
        /// <param name="ATaskCode">Code of the Task (depending on the TaskGroup this can be
        /// left empty)</param>
        /// <param name="ATaskParameter1">Parameter #1 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter2">Parameter #2 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter3">Parameter #3 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter4">Parameter #4 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskPriority">Priority of the Task</param>
        /// <param name="AExceptClientID">Pass in a Server-assigned ID of the Client that should
        /// not get the ClientTask in its queue. Makes sense only if -1 is used for the
        /// AClientID parameter. Default is -1, which means no Client is excepted.</param>
        /// <returns>The ClientID for which the ClientTask was queued (the last ClientID
        /// in the list of Clients if -1 is submitted for AClientID). Error values
        /// are: -1 if AClientID is identical with AExceptClientID, -2 if the
        /// Client specified with AClientID is not in the list of Clients or
        /// AClientID &lt;&gt; -1
        /// </returns>
        public Int32 QueueClientTaskFromClient(System.Int32 AClientID,
            String ATaskGroup,
            String ATaskCode,
            System.Object ATaskParameter1,
            System.Object ATaskParameter2,
            System.Object ATaskParameter3,
            System.Object ATaskParameter4,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            return QueueClientTask(
                AClientID, ATaskGroup, ATaskCode, ATaskParameter1, ATaskParameter2,
                ATaskParameter3, ATaskParameter4, ATaskPriority, AExceptClientID);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AClientID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskParameter1"></param>
        /// <param name="ATaskParameter2"></param>
        /// <param name="ATaskParameter3"></param>
        /// <param name="ATaskParameter4"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public Int32 QueueClientTaskFromClient(System.Int32 AClientID,
            String ATaskGroup,
            String ATaskCode,
            System.Object ATaskParameter1,
            System.Object ATaskParameter2,
            System.Object ATaskParameter3,
            System.Object ATaskParameter4,
            System.Int16 ATaskPriority)
        {
            return QueueClientTaskFromClient(AClientID,
                ATaskGroup,
                ATaskCode,
                ATaskParameter1,
                ATaskParameter2,
                ATaskParameter3,
                ATaskParameter4,
                ATaskPriority,
                -1);
        }

        /// make sure that not a null value is returned, but an empty string
        private static string ValueOrEmpty(string s)
        {
            if (s == null)
            {
                return "";
            }

            return s;
        }

        /// <summary>
        /// Formats the client list array for output in a fixed-width font (eg. to the
        /// Console)
        ///
        /// </summary>
        /// <returns>Formatted client list.
        /// </returns>
        public static String FormatClientList(Boolean AListDisconnectedClients)
        {
            String ClientLines;
            String ClientPort;
            ArrayList ClientsArrayList;
            Int16 PaddingFirstColumn;

            ClientsArrayList = TClientManager.ClientList(AListDisconnectedClients);

            if (ClientsArrayList.Count > 0)
            {
                ClientLines =
                    Catalog.GetString("  ID | Client          | Status           | Computer    | IP Address      | Type") +
                    Environment.NewLine +
                    "----+-----------------+------------------+-------------+-----------------+-----" +
                    Catalog.GetString("     | Connected since | Last activity    | Server Port |                 |");

                if (AListDisconnectedClients)
                {
                    // 'Last activity' column becomes 'Disconnected at' column
                    ClientLines = ClientLines.Replace("Last activity  ", "Disconnected at");
                }

                int ClientLine = 1;

                foreach (string[] currentClient in ClientsArrayList)
                {
                    if (!AListDisconnectedClients)
                    {
                        ClientPort = ValueOrEmpty(currentClient[7]);
                    }
                    else
                    {
                        // indicate that this Port is no longer used by the disconnected Client
                        ClientPort = '(' + ValueOrEmpty(currentClient[7]) + ')';
                    }

                    /*
                     * The following code is needed because the header lines go until column
                     #80 of the Console, and this causes the first Client line to be shifted
                     * two characters to the right...
                     */
                    if (ClientLine == 1)
                    {
                        PaddingFirstColumn = 2;
                    }
                    else
                    {
                        PaddingFirstColumn = 4;
                    }

                    ClientLines = ClientLines +
                                  ValueOrEmpty(currentClient[0]).PadLeft(PaddingFirstColumn) + " | " +
                                  ValueOrEmpty(currentClient[1]).PadRight(15) + " | " +
                                  ValueOrEmpty(currentClient[2]).PadRight(16) + " | " +
                                  ValueOrEmpty(currentClient[5]).PadRight(11) + " | " +
                                  ValueOrEmpty(currentClient[6]).PadRight(15) + " | " +
                                  ValueOrEmpty(currentClient[8]) + Environment.NewLine + "     | " +
                                  ValueOrEmpty(currentClient[3]).PadRight(15) + " | " +
                                  ValueOrEmpty(currentClient[4]).PadRight(16) + " | " +
                                  (ClientPort).PadRight(11) + " | " + Environment.NewLine;

                    ClientLine++;
                }
            }
            else
            {
                if (!AListDisconnectedClients)
                {
                    ClientLines = Catalog.GetString(" * no connected Clients *") + Environment.NewLine;
                }
                else
                {
                    ClientLines = Catalog.GetString(" * no disconnected Clients *") + Environment.NewLine;
                }
            }

            ClientLines = ClientLines +
                          String.Format(Catalog.GetString("  (Currently connected Clients: {0}; client connections since Server start: {1})"),
                ClientsConnected, ClientsConnectedTotal);
            return ClientLines;
        }

        /// <summary>
        /// Formats the client list array for output in the sysadm dialog for selection of a client id
        ///
        /// </summary>
        /// <returns>Formatted client list for sysadm dialog.
        /// </returns>
        public static String FormatClientListSysadm(Boolean AListDisconnectedClients)
        {
            String ReturnValue;

            //System.Int16 ClientLine;
            ArrayList ClientsArrayList;

            ClientsArrayList = TClientManager.ClientList(AListDisconnectedClients);
            ReturnValue = "";

            if (ClientsArrayList.Count > 0)
            {
                // the format is "22" "TIMOP" "23" "CHRISTIANK" (ClientId and UserName/Description)
                foreach (string[] currentClient in ClientsArrayList)
                {
                    ReturnValue = ReturnValue + " \"" +
                                  (currentClient[0]) + "\" \"" + // clientid
                                  (currentClient[1]).PadRight(15) + // username
                                  (currentClient[5]).PadRight(15) + "\""; // computer
                }
            }

            return ReturnValue;
        }

        #region TClientManager

        /// <summary>
        /// Initialises variables.
        ///
        /// </summary>
        /// <returns>void</returns>
        public TClientManager() : base()
        {
            FClientManagerCallForwarder = new TClientManagerCallForwarder(this);
            UClientObjects = SortedList.Synchronized(new SortedList());

            // Console.WriteLine('UClientObjects.IsSynchronized: ' + UClientObjects.IsSynchronized.ToString);
            FStartTime = DateTime.Now;
            UConnectClientMonitor = new System.Object();
            ClientManager.UAppDomainUnloadMonitor = new System.Object();
            UClientManagerObj = this;
        }

#if DEBUGMODE
        /// <summary>
        /// destructor
        /// </summary>
        ~TClientManager()
        {
            if (TSrvSetting.DL >= 5)
            {
                Console.WriteLine("TClientManager: Got collected after " + (new TimeSpan(
                                                                                DateTime.Now.Ticks - FStartTime.Ticks)).ToString() + " seconds.");
            }
        }
#endif



        /// <summary>
        /// needed for remoting
        /// </summary>
        /// <returns></returns>
        public override object InitializeLifetimeService()
        {
            // make sure that the TClientManager object exists until the Server stops!
            return null;
        }

        /// <summary>
        /// public for stateless (webservice) authentication
        /// </summary>
        /// <param name="AUserName"></param>
        /// <param name="APassword"></param>
        /// <param name="AClientComputerName"></param>
        /// <param name="AClientIPAddress"></param>
        /// <param name="AProcessID"></param>
        /// <param name="ASystemEnabled"></param>
        /// <returns></returns>
        static public TPetraPrincipal PerformLoginChecks(String AUserName,
            String APassword,
            String AClientComputerName,
            String AClientIPAddress,
            out Int32 AProcessID,
            out Boolean ASystemEnabled)
        {
            TPetraPrincipal ReturnValue;
            const String AUTHENTICATION_FAILED = "Authentication for User '{0}' failed! " +
                                                 "Reason: {1}. Connect request came from Computer '{2}' " + "(IP Address: {3})";

            try
            {
                // This function call will throw Exceptions if the User cannot be authenticated
                ReturnValue = Ict.Petra.Server.App.Core.Security.TUserManager.PerformUserAuthentication(AUserName,
                    APassword,
                    out AProcessID,
                    out ASystemEnabled);
            }
            catch (EUserNotExistantException)
            {
                TLogging.Log(String.Format(AUTHENTICATION_FAILED,
                        new String[] { AUserName, "User does not exist in DB", AClientComputerName, AClientIPAddress }));
                throw;
            }
            catch (EPasswordWrongException)
            {
                TLogging.Log(String.Format(AUTHENTICATION_FAILED,
                        new String[] { AUserName, "Password is wrong", AClientComputerName, AClientIPAddress }));

                // for security reasons we don't distinguish between a nonexisting user and a wrong password when informing the Client!
                throw new EUserNotExistantException("Invalid User ID/Password.");
            }
            catch (EAccessDeniedException)
            {
                TLogging.Log(String.Format(AUTHENTICATION_FAILED,
                        new String[] { AUserName, "User got auto-retired", AClientComputerName, AClientIPAddress }));
                throw;
            }
            catch (EUserRecordLockedException)
            {
                TLogging.Log(String.Format(AUTHENTICATION_FAILED,
                        new String[] { AUserName, "User record locked", AClientComputerName, AClientIPAddress }));
                throw;
            }
            catch (ESystemDisabledException)
            {
                TLogging.Log(String.Format(AUTHENTICATION_FAILED, new String[] { AUserName, "System Disabled", AClientComputerName, AClientIPAddress }));
                throw;
            }
            catch (Exception exp)
            {
                TLogging.Log(String.Format(AUTHENTICATION_FAILED,
                        new String[] { AUserName, "Exception occured: " + exp.ToString(), AClientComputerName, AClientIPAddress }));
                throw;
            }

            return ReturnValue;
        }

        /// <summary>
        /// Called by TClientManager to request the disconnection of a certain Client
        /// from the Petra Server.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client</param>
        /// <param name="ACantDisconnectReason">In case the function returns false, this
        /// contains the reason why the disconnection cannot take place.</param>
        /// <returns>true if disconnection will take place, otherwise false.
        /// </returns>
        public static bool ServerDisconnectClient(System.Int32 AClientID, out String ACantDisconnectReason)
        {
            bool ReturnValue;

            ReturnValue = false;
            try
            {
                if ((UClientObjects.Contains((object)AClientID))
                    && (((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus != TAppDomainStatus.adsStopped))
                {
                    // this.DisconnectClient would not work here since we are executing inside a static function...
                    ReturnValue = UClientManagerObj.DisconnectClient(AClientID, out ACantDisconnectReason);
                }
                else
                {
                    ACantDisconnectReason = "Client with ClientID: " + AClientID.ToString() + " not found in list of connected Clients!";
                    TLogging.Log("ServerDisconnectClient call: " + ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                    ReturnValue = false;
                }
            }
            catch (Exception Exp)
            {
                ACantDisconnectReason = "Exception occurred while disconnecting Client with ClientID: " + AClientID.ToString() + ": " + Exp.ToString();
                TLogging.Log("ServerDisconnectClient call: " + ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
            }
            return ReturnValue;
        }

        /// <summary>
        /// Called by TClientManager to queue a ClientTask for a certain Client.
        ///
        /// @comment The position in source code of this overload (*after* the first
        /// overload) is important! If this overload is put *before* the first
        /// overload, the Compiler won't compile the statement
        /// 'TCacheableTablesManager.Create(@TClientManager.QueueClientTask)'
        /// in the initialization section due to a Compiler bug (QualityCentral #7435)!
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client; use -1 to queue the
        /// ClientTask to all Clients</param>
        /// <param name="ATaskGroup">Group of the Task</param>
        /// <param name="ATaskCode">Code of the Task (depending on the TaskGroup this can be
        /// left empty)</param>
        /// <param name="ATaskPriority">Priority of the Task</param>
        /// <param name="AExceptClientID">Pass in a Server-assigned ID of the Client that should
        /// not get the ClientTask in its queue. Makes sense only if -1 is used for the
        /// AClientID parameter. Default is -1, which means no Client is excepted.</param>
        /// <returns>The ClientID for which the ClientTask was queued (the last ClientID
        /// in the list of Clients if -1 is submitted for AClientID). Error values
        /// are: -1 if AClientID is identical with AExceptClientID, -2 if the
        /// Client specified with AClientID is not in the list of Clients or
        /// AClientID &lt;&gt; -1
        /// </returns>
        public static Int32 QueueClientTask(System.Int32 AClientID,
            String ATaskGroup,
            String ATaskCode,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            return QueueClientTask(AClientID, ATaskGroup, ATaskCode, null, null, null, null, ATaskPriority, AExceptClientID);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AClientID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public static Int32 QueueClientTask(System.Int32 AClientID, String ATaskGroup, String ATaskCode, System.Int16 ATaskPriority)
        {
            return QueueClientTask(AClientID, ATaskGroup, ATaskCode, ATaskPriority, -1);
        }

        /// <summary>
        /// Called by TClientManager to queue a ClientTask for a certain Client.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client; use -1 to queue the
        /// ClientTask to all Clients</param>
        /// <param name="ATaskGroup">Group of the Task</param>
        /// <param name="ATaskCode">Code of the Task (depending on the TaskGroup this can be
        /// left empty)</param>
        /// <param name="ATaskParameter1">Parameter #1 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter2">Parameter #2 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter3">Parameter #3 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter4">Parameter #4 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskPriority">Priority of the Task</param>
        /// <param name="AExceptClientID">Pass in a Server-assigned ID of the Client that should
        /// not get the ClientTask in its queue. Makes sense only if -1 is used for the
        /// AClientID parameter. Default is -1, which means no Client is excepted.</param>
        /// <returns>The ClientID for which the ClientTask was queued (the last ClientID
        /// in the list of Clients if -1 is submitted for AClientID). Error values
        /// are: -1 if AClientID is identical with AExceptClientID, -2 if the
        /// Client specified with AClientID is not in the list of Clients or
        /// AClientID &lt;&gt; -1
        /// </returns>
        public static Int32 QueueClientTask(System.Int32 AClientID,
            String ATaskGroup,
            String ATaskCode,
            System.Object ATaskParameter1,
            System.Object ATaskParameter2,
            System.Object ATaskParameter3,
            System.Object ATaskParameter4,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            Int32 ReturnValue;
            TRunningAppDomain AppDomainEntry;
            IDictionaryEnumerator ClientEnum;

            ReturnValue = -2;

            if ((AClientID == -1) || (UClientObjects.Contains((object)AClientID)))
            {
                if (AClientID == (int)-1)
                {
                    ClientEnum = UClientObjects.GetEnumerator();
                    try
                    {
                        if (Monitor.TryEnter(UClientObjects.SyncRoot))
                        {
                            // Iterate over all Clients that are currently connected
                            while (ClientEnum.MoveNext())
                            {
                                AppDomainEntry = ((TRunningAppDomain)ClientEnum.Value);

                                // ...and the ClientID isn't the one to except from
                                if ((AppDomainEntry.ClientID != AExceptClientID)
                                    && ((AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsActive)
                                        || (AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsIdle)))
                                {
                                    ReturnValue = AppDomainEntry.ClientAppDomainConnection.ClientTaskAdd(ATaskGroup,
                                        ATaskCode,
                                        ATaskParameter1,
                                        ATaskParameter2,
                                        ATaskParameter3,
                                        ATaskParameter4,
                                        ATaskPriority);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(UClientObjects.SyncRoot);
                    }
                }
                else
                {
                    AppDomainEntry = (TRunningAppDomain)UClientObjects[(object)AClientID];

                    // Send Notification Message  if an AppDomain for the ClientID exists...
                    if (AppDomainEntry != null)
                    {
                        // ...and the ClientID isn't the one to except from
                        if ((AppDomainEntry.ClientID != AExceptClientID)
                            && ((AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsActive)
                                || (AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsIdle)))
                        {
                            ReturnValue = AppDomainEntry.ClientAppDomainConnection.ClientTaskAdd(ATaskGroup,
                                ATaskCode,
                                ATaskParameter1,
                                ATaskParameter2,
                                ATaskParameter3,
                                ATaskParameter4,
                                ATaskPriority);
                        }
                        else
                        {
                            ReturnValue = -1;
                        }
                    }
                }
            }
            else
            {
                ReturnValue = -2;
            }

            return ReturnValue;
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AClientID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskParameter1"></param>
        /// <param name="ATaskParameter2"></param>
        /// <param name="ATaskParameter3"></param>
        /// <param name="ATaskParameter4"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public static Int32 QueueClientTask(System.Int32 AClientID,
            String ATaskGroup,
            String ATaskCode,
            System.Object ATaskParameter1,
            System.Object ATaskParameter2,
            System.Object ATaskParameter3,
            System.Object ATaskParameter4,
            System.Int16 ATaskPriority)
        {
            return QueueClientTask(AClientID,
                ATaskGroup,
                ATaskCode,
                ATaskParameter1,
                ATaskParameter2,
                ATaskParameter3,
                ATaskParameter4,
                ATaskPriority,
                -1);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AUserID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public static Int32 QueueClientTask(String AUserID, String ATaskGroup, String ATaskCode, System.Int16 ATaskPriority)
        {
            return QueueClientTask(AUserID, ATaskGroup, ATaskCode, ATaskPriority, -1);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AUserID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskPriority"></param>
        /// <param name="AExceptClientID"></param>
        /// <returns></returns>
        public static Int32 QueueClientTask(String AUserID,
            String ATaskGroup,
            String ATaskCode,
            System.Int16 ATaskPriority,
            System.Int16 AExceptClientID)
        {
            return QueueClientTask(AUserID, ATaskGroup, ATaskCode,
                null, null, null, null, ATaskPriority);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AUserID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskParameter1"></param>
        /// <param name="ATaskParameter2"></param>
        /// <param name="ATaskParameter3"></param>
        /// <param name="ATaskParameter4"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public static Int32 QueueClientTask(String AUserID,
            String ATaskGroup,
            String ATaskCode,
            System.Object ATaskParameter1,
            System.Object ATaskParameter2,
            System.Object ATaskParameter3,
            System.Object ATaskParameter4,
            System.Int16 ATaskPriority)
        {
            return QueueClientTask(AUserID,
                ATaskGroup,
                ATaskCode,
                ATaskParameter1,
                ATaskParameter2,
                ATaskParameter3,
                ATaskParameter4,
                ATaskPriority,
                -1);
        }

        /// <summary>
        /// add client task to queue
        /// </summary>
        /// <param name="AUserID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskParameter1"></param>
        /// <param name="ATaskParameter2"></param>
        /// <param name="ATaskParameter3"></param>
        /// <param name="ATaskParameter4"></param>
        /// <param name="ATaskPriority"></param>
        /// <param name="AExceptClientID"></param>
        /// <returns></returns>
        public static Int32 QueueClientTask(String AUserID,
            String ATaskGroup,
            String ATaskCode,
            System.Object ATaskParameter1,
            System.Object ATaskParameter2,
            System.Object ATaskParameter3,
            System.Object ATaskParameter4,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            Int32 ReturnValue;
            IDictionaryEnumerator ClientEnum;
            TRunningAppDomain AppDomainEntry;

            ReturnValue = -2;
            ClientEnum = UClientObjects.GetEnumerator();

            try
            {
                if (Monitor.TryEnter(UClientObjects.SyncRoot))
                {
                    // Iterate over all Clients that are currently connected
                    while (ClientEnum.MoveNext())
                    {
                        AppDomainEntry = (TRunningAppDomain)ClientEnum.Value;

                        // Process Clients whose UserID is the one we look for
                        if (AppDomainEntry.UserID == AUserID)
                        {
                            // ...and the ClientID isn't the one to except from
                            if ((AppDomainEntry.ClientID != AExceptClientID)
                                && ((AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsActive)
                                    || (AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsIdle)))
                            {
#if DEBUGMODE
                                if (TSrvSetting.DL >= 5)
                                {
                                    Console.WriteLine(
                                        "TClientManager.QueueClientTask: queuing Task for UserID '" + AUserID + "' (ClientID: " +
                                        AppDomainEntry.ClientID.ToString());
                                }
#endif
                                ReturnValue = QueueClientTask(AppDomainEntry.ClientID,
                                    ATaskGroup,
                                    ATaskCode,
                                    ATaskParameter1,
                                    ATaskParameter2,
                                    ATaskParameter3,
                                    ATaskParameter4,
                                    ATaskPriority);
                            }
                            else
                            {
                                ReturnValue = -1;
                            }
                        }
                    }
                }
            }
            finally
            {
                Monitor.Exit(UClientObjects.SyncRoot);
            }

            return ReturnValue;
        }

        /// <summary>
        /// Called by TClientDomainManager to queue a ClientTask for a certain Client.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client; use -1 to queue the
        /// ClientTask to all Clients</param>
        /// <param name="ATaskGroup">Group of the Task</param>
        /// <param name="ATaskCode">Code of the Task</param>
        /// <param name="ATaskPriority">Priority of the Task</param>
        /// <param name="AExceptClientID">Pass in a Server-assigned ID of the Client that should
        /// not get the ClientTask in its queue. Makes sense only if -1 is used for the
        /// AClientID parameter. Default is -1, which means no Client is excepted.</param>
        /// <returns>The ClientID for which the ClientTask was queued (the last ClientID
        /// in the list of Clients if -1 is submitted for AClientID). Error values
        /// are: -1 if AClientID is identical with AExceptClientID, -2 if the
        /// Client specified with AClientID is not in the list of Clients or
        /// AClientID &lt;&gt; -1
        /// </returns>
        public Int32 QueueClientTaskFromClient(System.Int32 AClientID,
            String ATaskGroup,
            String ATaskCode,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            return QueueClientTask(AClientID, ATaskGroup, ATaskCode, null, null, null, null, ATaskPriority, AExceptClientID);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AClientID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public Int32 QueueClientTaskFromClient(System.Int32 AClientID, String ATaskGroup, String ATaskCode, System.Int16 ATaskPriority)
        {
            return QueueClientTaskFromClient(AClientID, ATaskGroup, ATaskCode, ATaskPriority, -1);
        }

        /// <summary>
        /// Called by TClientDomainManager to queue a ClientTask for a certain UserID.
        ///
        /// @comment If a User is several times connected, all Client instances for that UserID
        /// will get the ClientTask queued.
        ///
        /// </summary>
        /// <param name="AUserID">UserID for which the ClientTask should be queued</param>
        /// <param name="ATaskGroup">Group of the Task</param>
        /// <param name="ATaskCode">Code of the Task</param>
        /// <param name="ATaskPriority">Priority of the Task</param>
        /// <param name="AExceptClientID">Pass in a Server-assigned ID of the Client that should
        /// not get the ClientTask in its queue. Makes sense only if -1 is used for the
        /// AClientID parameter. Default is -1, which means no Client is excepted.</param>
        /// <returns>The ClientID for which the ClientTask was queued (the last ClientID
        /// in the list of Clients if -1 is submitted for AClientID). Error values
        /// are: -1 if AClientID is identical with AExceptClientID, -2 if the
        /// Client specified with AClientID is not in the list of Clients or
        /// AClientID &lt;&gt; -1
        /// </returns>
        public Int32 QueueClientTaskFromClient(String AUserID,
            String ATaskGroup,
            String ATaskCode,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            return QueueClientTask(AUserID, ATaskGroup, ATaskCode, null, null, null, null, ATaskPriority, AExceptClientID);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AUserID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public Int32 QueueClientTaskFromClient(String AUserID, String ATaskGroup, String ATaskCode, System.Int16 ATaskPriority)
        {
            return QueueClientTaskFromClient(AUserID, ATaskGroup, ATaskCode, ATaskPriority, -1);
        }

        /// <summary>
        /// Called by TClientDomainManager to queue a ClientTask for a certain UserID.
        ///
        /// @comment If a User is several times connected, all Client instances for that UserID
        /// will get the ClientTask queued.
        ///
        /// </summary>
        /// <param name="AUserID">UserID for which the ClientTask should be queued</param>
        /// <param name="ATaskGroup">Group of the Task</param>
        /// <param name="ATaskCode">Code of the Task (depending on the TaskGroup this can be
        /// left empty)</param>
        /// <param name="ATaskParameter1">Parameter #1 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter2">Parameter #2 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter3">Parameter #3 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskParameter4">Parameter #4 for the Task (depending on the TaskGroup
        /// this can be left empty)</param>
        /// <param name="ATaskPriority">Priority of the Task</param>
        /// <param name="AExceptClientID">Pass in a Server-assigned ID of the Client that should
        /// not get the ClientTask in its queue. Makes sense only if -1 is used for the
        /// AClientID parameter. Default is -1, which means no Client is excepted.</param>
        /// <returns>The ClientID for which the ClientTask was queued (the last ClientID
        /// in the list of Clients if -1 is submitted for AClientID). Error values
        /// are: -1 if AClientID is identical with AExceptClientID, -2 if the
        /// Client specified with AClientID is not in the list of Clients or
        /// AClientID &lt;&gt; -1
        /// </returns>
        public Int32 QueueClientTaskFromClient(String AUserID,
            String ATaskGroup,
            String ATaskCode,
            object ATaskParameter1,
            object ATaskParameter2,
            object ATaskParameter3,
            object ATaskParameter4,
            System.Int16 ATaskPriority,
            System.Int32 AExceptClientID)
        {
            return QueueClientTask(AUserID,
                ATaskGroup,
                ATaskCode,
                ATaskParameter1,
                ATaskParameter2,
                ATaskParameter3,
                ATaskParameter4,
                ATaskPriority,
                AExceptClientID);
        }

        /// <summary>
        /// overload
        /// </summary>
        /// <param name="AUserID"></param>
        /// <param name="ATaskGroup"></param>
        /// <param name="ATaskCode"></param>
        /// <param name="ATaskParameter1"></param>
        /// <param name="ATaskParameter2"></param>
        /// <param name="ATaskParameter3"></param>
        /// <param name="ATaskParameter4"></param>
        /// <param name="ATaskPriority"></param>
        /// <returns></returns>
        public Int32 QueueClientTaskFromClient(String AUserID,
            String ATaskGroup,
            String ATaskCode,
            object ATaskParameter1,
            object ATaskParameter2,
            object ATaskParameter3,
            object ATaskParameter4,
            System.Int16 ATaskPriority)
        {
            return QueueClientTaskFromClient(AUserID,
                ATaskGroup,
                ATaskCode,
                ATaskParameter1,
                ATaskParameter2,
                ATaskParameter3,
                ATaskParameter4,
                ATaskPriority,
                -1);
        }

        /// <summary>
        /// add error to log, using Ict.Petra.Server.App.Core.Security.TErrorLog.AddErrorLogEntry
        /// </summary>
        /// <param name="AErrorCode"></param>
        /// <param name="AContext"></param>
        /// <param name="AMessageLine1"></param>
        /// <param name="AMessageLine2"></param>
        /// <param name="AMessageLine3"></param>
        /// <param name="AUserID"></param>
        /// <param name="AProcessID"></param>
        public void AddErrorLogEntry(String AErrorCode,
            String AContext,
            String AMessageLine1,
            String AMessageLine2,
            String AMessageLine3,
            String AUserID,
            Int32 AProcessID)
        {
            TVerificationResultCollection VerificationResult;

            Ict.Petra.Server.App.Core.Security.TErrorLog.AddErrorLogEntry(AErrorCode,
                AContext,
                AMessageLine1,
                AMessageLine2,
                AMessageLine3,
                AUserID,
                AProcessID,
                out VerificationResult);
        }

        /// <summary>
        /// Builds an array that contains information about the Clients that are currently
        /// connected to the Petra Server or were connected to the Petra Server at some
        /// time in the past.
        ///
        /// </summary>
        /// <param name="AListDisconnectedClients">Lists only connected Clients if false and
        /// only disconnected Clients if true.</param>
        /// <returns>A two-dimensional String Array containing the
        /// </returns>
        public static ArrayList BuildClientList(Boolean AListDisconnectedClients)
        {
            ArrayList ClientList = new ArrayList();
            IDictionaryEnumerator ClientEnum;

            System.Int16 ClientCounter;
            String AppDomainStatusString;
            String ClientServerConnectionTypeString;
            DateTime LastActionTime;
            DateTime LastClientAction;
            try
            {
                if (UClientObjects != null)
                {
                    ClientCounter = 0;
                    ClientEnum = UClientObjects.GetEnumerator();
                    try
                    {
                        if (Monitor.TryEnter(UClientObjects.SyncRoot))
                        {
                            // Iterate over all Clients
                            while (ClientEnum.MoveNext())
                            {
                                TRunningAppDomain app = ((TRunningAppDomain)ClientEnum.Value);

                                if (((AListDisconnectedClients)
                                     && ((app.FAppDomainStatus == TAppDomainStatus.adsActive)
                                         || (app.FAppDomainStatus == TAppDomainStatus.adsIdle)
                                         || (app.FAppDomainStatus == TAppDomainStatus.adsConnectingLoginVerification)
                                         || (app.FAppDomainStatus == TAppDomainStatus.adsConnectingLoginOK)
                                         || (app.FAppDomainStatus == TAppDomainStatus.adsConnectingAppDomainSetupOK)))
                                    || ((!AListDisconnectedClients)
                                        && (!((app.FAppDomainStatus == TAppDomainStatus.adsActive)
                                              || (app.FAppDomainStatus == TAppDomainStatus.adsIdle)
                                              || (app.FAppDomainStatus == TAppDomainStatus.adsConnectingLoginVerification)
                                              || (app.FAppDomainStatus == TAppDomainStatus.adsConnectingLoginOK)
                                              || (app.FAppDomainStatus == TAppDomainStatus.adsConnectingAppDomainSetupOK)))))
                                {
                                    // Client has got the wrong AppDomainStatus > skip it
                                    continue;
                                }

                                ClientCounter++;

                                LastActionTime = DateTime.MinValue;

                                if ((app.FAppDomainStatus == TAppDomainStatus.adsActive) || (app.FAppDomainStatus == TAppDomainStatus.adsIdle))
                                {
                                    try
                                    {
                                        LastActionTime = app.FClientAppDomainConnection.LastActionTime;
                                    }
                                    catch (System.Runtime.Remoting.RemotingException)
                                    {
                                        LastClientAction = DateTime.MinValue;
                                    }
                                    catch (Exception)
                                    {
                                        throw;
                                    }
                                }

                                // Determine/update Client AppDomain's Status
                                if (app.FAppDomainStatus == TAppDomainStatus.adsConnectingLoginVerification)
                                {
                                    AppDomainStatusString = "Connecting...(1)";
                                    LastClientAction = app.FClientConnectionStartTime;
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsConnectingLoginOK)
                                {
                                    AppDomainStatusString = "Connecting...(2)";
                                    LastClientAction = app.FClientConnectionStartTime;
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsConnectingAppDomainSetupOK)
                                {
                                    AppDomainStatusString = "Connecting...(3)";
                                    LastClientAction = app.FClientConnectionStartTime;
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsActive)
                                {
                                    if (LastActionTime.AddMinutes(1) > DateTime.Now)
                                    {
                                        AppDomainStatusString = "Active";
                                        LastClientAction = LastActionTime;
                                    }
                                    else
                                    {
                                        AppDomainStatusString = "Idle";
                                        app.FAppDomainStatus = TAppDomainStatus.adsIdle;
                                        LastClientAction = LastActionTime;
                                    }
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsIdle)
                                {
                                    if (LastActionTime.AddMinutes(1) < DateTime.Now)
                                    {
                                        AppDomainStatusString = "Idle";
                                        LastClientAction = LastActionTime;
                                    }
                                    else
                                    {
                                        AppDomainStatusString = "Active";
                                        app.FAppDomainStatus = TAppDomainStatus.adsActive;
                                        LastClientAction = LastActionTime;
                                    }
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsDisconnectingDBClosing)
                                {
                                    AppDomainStatusString = "Disconnecting(1)";
                                    LastClientAction = app.FClientDisconnectionStartTime;
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsDisconnectingAppDomainUnloading)
                                {
                                    AppDomainStatusString = "Disconnecting(2)";
                                    LastClientAction = app.FClientDisconnectionStartTime;
                                }
                                else if (app.FAppDomainStatus == TAppDomainStatus.adsStopped)
                                {
                                    AppDomainStatusString = "Disconnected!";
                                    LastClientAction = app.FClientDisconnectionFinishedTime;
                                }
                                else
                                {
                                    AppDomainStatusString = "[Unknown]";
                                    LastClientAction = DateTime.MinValue;
                                }

                                if (app.FClientServerConnectionType == TClientServerConnectionType.csctRemote)
                                {
                                    ClientServerConnectionTypeString = "Rem.";
                                }
                                else if (app.FClientServerConnectionType == TClientServerConnectionType.csctLocal)
                                {
                                    ClientServerConnectionTypeString = "Locl";
                                }
                                else
                                {
                                    ClientServerConnectionTypeString = "LAN";
                                }

                                // Fill array with Client formatted information

                                //String[] currentClient = (String[])ClientList[ClientCounter];
                                String[] currentClient = new string[9];

                                currentClient[0] = app.FClientID.ToString();
                                currentClient[1] = app.FClientName;
                                currentClient[2] = AppDomainStatusString;
                                currentClient[3] = app.FClientConnectionStartTime.ToString("dd/MM HH:mm:ss");

                                if (LastClientAction != DateTime.MinValue)
                                {
                                    currentClient[4] = LastClientAction.ToString("dd/MM HH:mm:ss");
                                }
                                else
                                {
                                    currentClient[4] = "N/A";
                                }

                                currentClient[5] = app.FClientComputerName;
                                currentClient[6] = app.FClientIPAddress;

                                if (app.FAppDomainRemotingPort != 0)
                                {
                                    currentClient[7] = app.FAppDomainRemotingPort.ToString();
                                }
                                else
                                {
                                    currentClient[7] = "N/A";
                                }

                                currentClient[8] = ClientServerConnectionTypeString;

                                ClientList.Add(currentClient);
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(UClientObjects.SyncRoot);
                    }
                }
            }
            catch (Exception exp)
            {
                TLogging.Log("Error while building Client list!  Exception: " + exp.ToString(), TLoggingType.ToConsole | TLoggingType.ToLogfile);
                throw;
            }

            return ClientList;
        }

        /// <summary>
        /// Called by TServerManager to request an array that contains information about
        /// the Clients that are currently connected to the Petra Server.
        ///
        /// </summary>
        /// <param name="AListDisconnectedClients">Lists only connected Clients if false and
        /// only disconnected Clients if true.
        /// </param>
        /// <returns>void</returns>
        public static ArrayList ClientList(Boolean AListDisconnectedClients)
        {
            return BuildClientList(AListDisconnectedClients);
        }

        /// <summary>
        /// Called by a Client to request connection to the Petra Server.
        ///
        /// Creates an AppDomain, loads Petra Module DLL's into it and returns
        /// .NET Remoting URLs for intantiated objects that represent the Petra Module
        /// Root Namespaces (eg. MPartner, MFinance).
        ///
        /// </summary>
        /// <param name="AUserName">Username with which the Client connects</param>
        /// <param name="APassword">Password with which the Client connects</param>
        /// <param name="AClientComputerName">Computer name of the Client</param>
        /// <param name="AClientExeVersion"></param>
        /// <param name="AClientIPAddress">IP Address of the Client</param>
        /// <param name="AClientServerConnectionType">Type of the connection (eg. LAN, Remote)</param>
        /// <param name="AClientName">Server-assigned Name of the Client</param>
        /// <param name="AClientID">Server-assigned ID of the Client</param>
        /// <param name="ARemotingPort">Server-assigned .NET Remoting IP Port to which the
        /// Client's Remoting calls must be directed</param>
        /// <param name="ARemotingURLs">A HashTable containing .NET Remoting URLs of the Petra
        /// Module Root Namespaces (eg. MPartner, MFinance) and other important objects
        /// that need to be called from the Client.</param>
        /// <param name="AServerOS">Operating System that the Server is running on
        /// </param>
        /// <param name="AProcessID"></param>
        /// <param name="AWelcomeMessage"></param>
        /// <param name="ASystemEnabled"></param>
        /// <param name="AUserInfo"></param>
        /// <returns>void</returns>
        public void ConnectClient(String AUserName,
            String APassword,
            String AClientComputerName,
            String AClientIPAddress,
            System.Version AClientExeVersion,
            TClientServerConnectionType AClientServerConnectionType,
            out String AClientName,
            out System.Int32 AClientID,
            out System.Int16 ARemotingPort,
            out Hashtable ARemotingURLs,
            out TExecutingOSEnum AServerOS,
            out Int32 AProcessID,
            out String AWelcomeMessage,
            out Boolean ASystemEnabled,
            out TPetraPrincipal AUserInfo)
        {
            String LoadInAppDomainName;
            TClientAppDomainConnection ClientDomainManager = null;
            String RemotingURL_RemotedObject = "";
            String RemotingURL_PollClientTasks;
            String RemotingURL_MSysMan;
            String RemotingURL_MPartner;
            String RemotingURL_MPersonnel;
            String RemotingURL_MFinance;
            String RemotingURL_MReporting;
            TRunningAppDomain AppDomainEntry;
            String CantDisconnectReason;

#if DEBUGMODE
            if (TSrvSetting.DL >= 10)
            {
                TLogging.Log(
                    "Loaded Assemblies in AppDomain " + Thread.GetDomain().FriendlyName + " (at call of ConnectClient):", TLoggingType.ToConsole |
                    TLoggingType.ToLogfile);

                foreach (Assembly tmpAssembly in Thread.GetDomain().GetAssemblies())
                {
                    TLogging.Log(tmpAssembly.FullName, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                }
            }
#endif

            /*
             * Every Client Connection request is coming in in a separate Thread
             * (.NET Remoting does that for us and this is good!). However, the next block
             * of code must be executed only by exactly ONE thread at the same time to
             * preserve the integrity of Client tracking!
             */
            try
            {
                if (Monitor.TryEnter(UConnectClientMonitor, TSrvSetting.ClientConnectionTimeoutAfterXSeconds * 1000))
                {
                    #region Logging
#if DEBUGMODE
                    if (TSrvSetting.DL >= 4)
                    {
                        Console.WriteLine(FormatClientList(false));
                        Console.WriteLine(FormatClientList(true));
                    }
#endif

                    if (TSrvSetting.DL >= 4)
                    {
                        TLogging.Log("Client '" + AUserName + "' is connecting...", TLoggingType.ToConsole | TLoggingType.ToLogfile);
                    }
                    else
                    {
                        TLogging.Log("Client '" + AUserName + "' is connecting...", TLoggingType.ToLogfile);
                    }

                    #endregion
                    #region Variable assignments
                    ARemotingURLs = new Hashtable(6);
                    AClientID = (short)FClientsConnectedTotal;
                    FClientsConnectedTotal++;
                    AClientName = AUserName.ToUpper() + "_" + AClientID.ToString();
                    ARemotingPort = FindFreeRemotingPort();
                    AServerOS = TSrvSetting.ExecutingOS;
                    LoadInAppDomainName = AClientName + "_Domain";
                    #endregion
                    try
                    {
                        if (Monitor.TryEnter(UClientObjects.SyncRoot))
                        {
                            // Add the new Client to UClientObjects SortedList
                            UClientObjects.Add((object)AClientID,
                                new TRunningAppDomain(AClientID, AUserName.ToUpper(), AClientName, AClientComputerName, AClientIPAddress,
                                    AClientServerConnectionType, LoadInAppDomainName));
                        }
                    }
                    finally
                    {
                        Monitor.Exit(UClientObjects.SyncRoot);
                    }

                    #region Client Version vs. Server Version check
#if DEBUGMODE
                    if (TSrvSetting.DL >= 9)
                    {
                        Console.WriteLine(
                            "Client EXE Program Version: " + AClientExeVersion.ToString() + "; Server EXE Program Version: " +
                            TSrvSetting.ApplicationVersion.ToString());
                    }
#endif

                    if (AClientExeVersion != TSrvSetting.ApplicationVersion)
                    {
                        ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsStopped;
                        #region Logging

                        if (TSrvSetting.DL >= 4)
                        {
                            TLogging.Log(
                                "Client '" + AUserName + "' tried to connect, but its Program Version (" + AClientExeVersion.ToString() +
                                ") doesn't match! Aborting Client Connection!", TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        }
                        else
                        {
                            TLogging.Log(
                                "Client '" + AUserName + "' tried to connect, but its Program Version (" + AClientExeVersion.ToString() +
                                ") doesn't match! Aborting Client Connection!", TLoggingType.ToLogfile);
                        }

                        #endregion
                        throw new EClientVersionMismatchException(String.Format(StrClientServerExeProgramVersionMismatchMessage,
                                AClientExeVersion.ToString(), TSrvSetting.ApplicationVersion.ToString()));
                    }

                    #endregion

                    #region Login request verification (incl. User authentication)

                    // Perform login checks such as User authentication and Site Key check
                    try
                    {
                        AUserInfo = PerformLoginChecks(AUserName,
                            APassword,
                            AClientComputerName,
                            AClientIPAddress,
                            out AProcessID,
                            out ASystemEnabled);
                    }
                    catch (EPetraSecurityException)
                    #region Exception handling
                    {
                        #region Logging

                        if (TSrvSetting.DL >= 4)
                        {
                            TLogging.Log("Client '" + AUserName + "' tried to connect, but it failed the Login Checks. Aborting Client Connection!",
                                TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        }
                        else
                        {
                            TLogging.Log("Client '" + AUserName + "' tried to connect, but it failed the Login Checks. Aborting Client Connection!",
                                TLoggingType.ToLogfile);
                        }

                        #endregion
                        ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsStopped;
                        try
                        {
                            if (DBAccess.GDBAccessObj != null)
                            {
                                DBAccess.GDBAccessObj.RollbackTransaction();
                            }
                        }
                        catch (System.InvalidOperationException)
                        {
                            // ignore this exception since the RollBack is just a safety net,
                            // and if it fails it means there was no running transaction.
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                        throw;
                    }
                    catch (Exception)
                    {
                        ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsStopped;
                        try
                        {
                            DBAccess.GDBAccessObj.RollbackTransaction();
                        }
                        catch (System.InvalidOperationException)
                        {
                            // ignore this exception since the RollBack is just a safety net,
                            // and if it fails it means there was no running transaction.
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                        throw;
                    }
                    #endregion

                    // Login Checks were successful!
                    ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsConnectingLoginOK;

                    // Retrieve Welcome message
                    try
                    {
                        AWelcomeMessage = TMaintenanceLogonMessage.GetLogonMessage(AUserInfo.PetraIdentity.LanguageCode, true);
                    }
                    catch (Exception)
                    #region Exception handling
                    {
                        ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsStopped;
                        try
                        {
                            DBAccess.GDBAccessObj.RollbackTransaction();
                        }
                        catch (System.InvalidOperationException)
                        {
                        }
                        // ignore this exception since the RollBack is just a safety net,
                        // and if it fails it means there was no running transaction.
                        catch (Exception)
                        {
                            throw;
                        }
                        throw;
                    }
                    #endregion
                    #endregion
#if DEBUGMODE
                    if (TSrvSetting.DL >= 10)
                    {
                        TLogging.Log(
                            "Loaded Assemblies in AppDomain " + Thread.GetDomain().FriendlyName + " (before new AppDomain load):",
                            TLoggingType.ToConsole | TLoggingType.ToLogfile);

                        foreach (Assembly tmpAssembly in Thread.GetDomain().GetAssemblies())
                        {
                            TLogging.Log(tmpAssembly.FullName, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        }
                    }
#endif
                    #region Create new AppDomain for Client, load ClientDomain DLL into it, initialise AppDomain
                    try
                    {
                        try
                        {
                            // The following statement creates a new AppDomain for the connecting
                            // Client and remotes an instance of TRemoteLoader into it.
                            ClientDomainManager = new TClientAppDomainConnection(AClientName);
#if DEBUGMODE
                            if (TSrvSetting.DL >= 10)
                            {
                                TLogging.Log(
                                    "Loaded Assemblies in AppDomain " + Thread.GetDomain().FriendlyName +
                                    ": (after TClientAppDomainConnection.Create)",
                                    TLoggingType.ToConsole | TLoggingType.ToLogfile);

                                foreach (Assembly tmpAssembly in Thread.GetDomain().GetAssemblies())
                                {
                                    TLogging.Log(tmpAssembly.FullName, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                }
                            }
                            // Console.ReadLine;
#endif

                            // The following statement loads the ClientDomain DLL into the Client's
                            // AppDomain, instantiates the main Class and initialises the AppDomain
                            ClientDomainManager.LoadDomainManagerAssembly(AClientID,
                                ARemotingPort,
                                AClientServerConnectionType,
                                FClientManagerCallForwarder,
                                USystemDefaultsCache,
                                UCacheableTablesManager,
                                AUserInfo,
                                out RemotingURL_PollClientTasks,
                                out RemotingURL_RemotedObject);
                            ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_TESTOBJECT, RemotingURL_RemotedObject);
                            ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_POLLCLIENTTASKS, RemotingURL_PollClientTasks);
                        }
                        catch (TargetInvocationException exp)
                        {
                            TLogging.Log(
                                "Error while creating new AppDomain for Client! Port: " + ARemotingPort.ToString() + " Exception: " +
                                exp.ToString() +
                                "\r\n" + "InnerException: " + exp.InnerException.ToString());
                            throw exp.InnerException;
                        }
                        catch (Exception exp)
                        {
                            TLogging.Log(
                                "Error while creating new AppDomain for Client! Port: " + ARemotingPort.ToString() + " Exception: " + exp.ToString());
                            throw;
                        }
                    }
                    catch (Exception exp)
                    {
                        // we should cleanly shut down the appdomain, to avoid exception:
                        // System.Net.Sockets.SocketException: Address already in use
                        AppDomainEntry = (TRunningAppDomain)UClientObjects[(object)AClientID];

                        if ((AppDomainEntry == null) || (ClientDomainManager == null))
                        {
                            // Application domain was not setup yet
                            AppDomainEntry.AppDomainStatus = TAppDomainStatus.adsStopped;
                        }
                        else
                        {
                            AppDomainEntry.PassInClientRemotingInfo(RemotingURL_RemotedObject, ARemotingPort, ClientDomainManager);
                            ServerDisconnectClient(AClientID, out CantDisconnectReason);
                        }

                        throw exp;
                    }
                    #endregion
#if DEBUGMODE
                    if (TSrvSetting.DL >= 10)
                    {
                        TLogging.Log(
                            "Loaded Assemblies in AppDomain " + Thread.GetDomain().FriendlyName + " (after new AppDomain load):",
                            TLoggingType.ToConsole |
                            TLoggingType.ToLogfile);

                        foreach (Assembly tmpAssembly in Thread.GetDomain().GetAssemblies())
                        {
                            TLogging.Log(tmpAssembly.FullName, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        }
                    }
#endif
                    ((TRunningAppDomain)UClientObjects[(object)AClientID]).PassInClientRemotingInfo(RemotingURL_RemotedObject,
                        ARemotingPort,
                        ClientDomainManager);
                    ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsConnectingAppDomainSetupOK;

                    /*
                     * Uncomment the following statement to be able to better test how the
                     * Client reacts when it tries to connect and receives a
                     * ELoginFailedServerTooBusyException.
                     */

                    // Thread.Sleep(7000);

                    /*
                     * Notify all waiting Clients (that have not timed out yet) that they can
                     * now try to connect...
                     */
                    Monitor.PulseAll(UConnectClientMonitor);
                }
                else
                {
                    /*
                     * Throw Exception to tell any timed-out connecting Client that the Server
                     * is too busy to accept connect requests at the moment.
                     */
                    throw new ELoginFailedServerTooBusyException();
                }
            }
            finally
            {
                Monitor.Exit(UConnectClientMonitor);
            }
            #region Load Petra Module DLLs into Clients AppDomain, initialise them and remote an Instantiator Object

            // Load SYSMAN Module assembly (always loaded)
            ClientDomainManager.LoadPetraModuleAssembly(SharedConstants.REMOTINGURL_IDENTIFIER_MSYSMAN, out RemotingURL_MSysMan);
            ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_MSYSMAN, RemotingURL_MSysMan);
#if DEBUGMODE
            if (TSrvSetting.DL >= 5)
            {
                Console.WriteLine("  TMSysMan instantiated. Remoting URL: " + RemotingURL_MSysMan);
            }
#endif

            // Load PARTNER Module assembly (always loaded)
            ClientDomainManager.LoadPetraModuleAssembly(SharedConstants.REMOTINGURL_IDENTIFIER_MPARTNER, out RemotingURL_MPartner);
            ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_MPARTNER, RemotingURL_MPartner);
#if DEBUGMODE
            if (TSrvSetting.DL >= 5)
            {
                Console.WriteLine("  TMPartner instantiated. Remoting URL: " + RemotingURL_MPartner);
            }
#endif

            // Load REPORTING Module assembly (always loaded)
            ClientDomainManager.LoadPetraModuleAssembly(SharedConstants.REMOTINGURL_IDENTIFIER_MREPORTING, out RemotingURL_MReporting);
            ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_MREPORTING, RemotingURL_MReporting);
#if DEBUGMODE
            if (TSrvSetting.DL >= 5)
            {
                Console.WriteLine("  TMReporting instantiated. Remoting URL: " + RemotingURL_MReporting);
            }
#endif

            // Load PERSONNEL Module assembly (loaded only for users that have personnel privileges)
            if (AUserInfo.IsInModule(SharedConstants.PETRAMODULE_PERSONNEL))
            {
                ClientDomainManager.LoadPetraModuleAssembly(SharedConstants.REMOTINGURL_IDENTIFIER_MPERSONNEL, out RemotingURL_MPersonnel);
                ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_MPERSONNEL, RemotingURL_MPersonnel);
#if DEBUGMODE
                if (TSrvSetting.DL >= 5)
                {
                    Console.WriteLine("  TMPersonnel instantiated. Remoting URL: " + RemotingURL_MPersonnel);
                }
#endif
            }

            // Load FINANCE Module assembly (loaded only for users that have finance privileges)
            if ((AUserInfo.IsInModule(SharedConstants.PETRAMODULE_FINANCE1)) || (AUserInfo.IsInModule(SharedConstants.PETRAMODULE_FINANCE2))
                || (AUserInfo.IsInModule(SharedConstants.PETRAMODULE_FINANCE3)))
            {
                ClientDomainManager.LoadPetraModuleAssembly(SharedConstants.REMOTINGURL_IDENTIFIER_MFINANCE, out RemotingURL_MFinance);
                ARemotingURLs.Add(SharedConstants.REMOTINGURL_IDENTIFIER_MFINANCE, RemotingURL_MFinance);
#if DEBUGMODE
                if (TSrvSetting.DL >= 5)
                {
                    Console.WriteLine("  TMFinance instantiated. Remoting URL: " + RemotingURL_MFinance);
                }
#endif
            }

            #endregion
            ((TRunningAppDomain)UClientObjects[(object)AClientID]).AppDomainStatus = TAppDomainStatus.adsActive;
            ((TRunningAppDomain)UClientObjects[(object)AClientID]).FClientConnectionFinishedTime = DateTime.Now;
            #region Logging

            //
            // Assemblies successfully loaded into Client AppDomain
            //
            if (TSrvSetting.DL >= 4)
            {
                TLogging.Log(
                    "Client '" + AUserName + "' successfully connected (took " +
                    ((TRunningAppDomain)UClientObjects[(object)AClientID]).FClientConnectionFinishedTime.Subtract(
                        ((TRunningAppDomain)UClientObjects[(
                                                               object)
                                                           AClientID
                         ]).FClientConnectionStartTime).
                    TotalSeconds.ToString() + " sec). ClientID: " + AClientID.ToString(),
                    TLoggingType.ToConsole | TLoggingType.ToLogfile);
            }
            else
            {
                TLogging.Log("Client '" + AUserName + "' successfully connected. ClientID: " + AClientID.ToString(), TLoggingType.ToLogfile);
            }

            #endregion
        }

        /// <summary>
        /// Called by a Client to request disconnection from the Petra Server.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client that should be disconnected</param>
        /// <param name="ACantDisconnectReason">In case the function returns false, this
        /// contains the reason why the disconnection cannot take place.</param>
        /// <returns>true if disconnection will take place, otherwise false.
        /// </returns>
        public Boolean DisconnectClient(System.Int32 AClientID, out String ACantDisconnectReason)
        {
            return DisconnectClient(AClientID, "Client closed", out ACantDisconnectReason);
        }

        /// <summary>
        /// Called by a Client to request disconnection from the Petra Server.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client that should be disconnected</param>
        /// <param name="AReason"></param>
        /// <param name="ACantDisconnectReason">In case the function returns false, this
        /// contains the reason why the disconnection cannot take place.</param>
        /// <returns>true if disconnection will take place, otherwise false.
        /// </returns>
        public Boolean DisconnectClient(System.Int32 AClientID, String AReason, out String ACantDisconnectReason)
        {
            Boolean ReturnValue;
            TRunningAppDomain AppDomainEntry;
            TDisconnectClientThread DisconnectClientThreadObject;
            Thread DisconnectionThread;

            ACantDisconnectReason = "";

#if DEBUGMODE
            if (TSrvSetting.DL >= 4)
            {
                TLogging.Log("Trying to disconnect client (ClientID: " + AClientID.ToString() + ") for the reason: " + AReason);
            }
#endif
            AppDomainEntry = (TRunningAppDomain)UClientObjects[(object)AClientID];

            if (AppDomainEntry == null)
            {
                // avoid a crash
                TLogging.Log("Warning: trying to disconnect a non existing client; nothing is done");
                return false;
            }

            try
            {
                if (Monitor.TryEnter(AppDomainEntry.DisconnectClientMonitor, 5000))
                {
                    try
                    {
                        // Tear down the AppDomain  if an AppDomain for the ClientID exists.
                        if (!(AppDomainEntry == null))
                        {
                            if (AppDomainEntry.ClientAppDomainConnection != null)
                            {
                                // Only perform tear down if it is not already in progress!
                                if (!(AppDomainEntry.ClientDisconnectionScheduled))
                                {
                                    AppDomainEntry.ClientDisconnectionScheduled = true;
                                    DisconnectClientThreadObject = new TDisconnectClientThread();
                                    DisconnectClientThreadObject.AppDomainEntry = AppDomainEntry;
                                    DisconnectClientThreadObject.ClientID = AClientID;
                                    DisconnectClientThreadObject.Reason = AReason;

                                    // Start thread that does the actual disconnection
                                    DisconnectionThread = new Thread(new ThreadStart(DisconnectClientThreadObject.StartClientDisconnection));

                                    if (TSrvSetting.DL >= 4)
                                    {
                                        TLogging.Log(
                                            "Client disconnection Thread is about to be started for " + "'" + AppDomainEntry.FClientName +
                                            "' (ClientID: " + AppDomainEntry.FClientID.ToString() + ')' + "...",
                                            TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                    }

                                    DisconnectionThread.Start();
                                    ReturnValue = true;
                                }
                                else
                                {
                                    ACantDisconnectReason = "Can't disconnect ClientID " + AClientID.ToString() +
                                                            ": Client is already disconnecting!";
                                    TLogging.Log(ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                    ReturnValue = false;
                                }
                            }
                            // AppDomainEntry.ClientAppDomainConnection <> nil
                            else
                            {
                                if ((AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsConnectingLoginVerification)
                                    || (AppDomainEntry.AppDomainStatus == TAppDomainStatus.adsConnectingLoginOK))
                                {
                                    ACantDisconnectReason = "Can't disconnect ClientID " + AClientID.ToString() +
                                                            ": Client is in the process of connecting!";
                                }
                                else
                                {
                                    ACantDisconnectReason = "Can't disconnect ClientID " + AClientID.ToString() + ": Client is already disconnected!";
                                }

                                TLogging.Log(ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                ReturnValue = false;
                            }
                        }
                        // not (AppDomainEntry = nil)
                        else
                        {
                            if (UClientObjects.Contains((object)AClientID))
                            {
                                ACantDisconnectReason = "Can't disconnect ClientID: " + AClientID.ToString() +
                                                        ": Client List entry for this ClientID is empty (should not happen)!";
                                TLogging.Log(ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                ReturnValue = false;
                            }
                            else
                            {
                                ACantDisconnectReason = "Can't disconnect ClientID: " + AClientID.ToString() +
                                                        ": Client List entry for this ClientID doesn't exist (should not happen)!";
                                TLogging.Log(ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                ReturnValue = false;
                            }
                        }
                    }
                    catch (Exception Exp)
                    {
                        ACantDisconnectReason = "DisconnectClient call for ClientID: " + AClientID.ToString() + ": Exception occured: " +
                                                Exp.ToString();
                        TLogging.Log(ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        ReturnValue = false;
                    }
                }
                else
                {
                    ACantDisconnectReason = "Can't disconnect ClientID: " + AClientID.ToString() +
                                            ": Client is already being scheduled for disconnection!";
                    TLogging.Log(ACantDisconnectReason, TLoggingType.ToConsole | TLoggingType.ToLogfile);
                    ReturnValue = false;

                    // don't do anything else here since one attempt to start the client
                    // disconnection is enough!
                }
            }
            finally
            {
                Monitor.Exit(AppDomainEntry.DisconnectClientMonitor);
            }
            return ReturnValue;
        }

        /// <summary>
        /// Find the lowest unused Port number in the SortedList, thereby re-using the
        /// Ports of already disconnected clients.
        ///
        /// @comment Thread safety: The finding of a free remoting port is not
        /// thread-save (iteration through a collection is not thread-save!). Therefore
        /// this function must be called from a piece of code that makes sure that this
        /// function is called in a way that ensures that the collection cannot change
        /// while this function is executing (this is ensured in ConnectClient).
        ///
        /// </summary>
        /// <returns>void</returns>
        private System.Int16 FindFreeRemotingPort()
        {
            TRunningAppDomain AppDomainEntry;
            IDictionaryEnumerator ClientEnum;
            ArrayList ClientPortArray = new ArrayList();
            Int16 ClientCounter;
            Int16 PortCounter;
            Int16 FreeRemotingPort;
            Int16 PotentiallyFreeRemotingPort;
            Int16 LowestAvailableRemotingPort;

            FreeRemotingPort = -1;
            LowestAvailableRemotingPort = (short)(TSrvSetting.BaseIPAddress + 1);
            ClientEnum = UClientObjects.GetEnumerator();

            if (UClientObjects.Count != 0)
            {
                ClientCounter = 0;
                try
                {
                    if (Monitor.TryEnter(UClientObjects.SyncRoot))
                    {
                        /*
                         * Iterate over all Clients that are currently connected
                         * Iteration occurs ascending [from client with the lowest ClientID to the client with the highest ClientID]
                         */
                        while (ClientEnum.MoveNext())
                        {
                            if (((TRunningAppDomain)ClientEnum.Value).AppDomainStatus != TAppDomainStatus.adsStopped)
                            {
                                AppDomainEntry = ((TRunningAppDomain)ClientEnum.Value);
                                ClientPortArray.Add((short)AppDomainEntry.AppDomainRemotingPort);
                                ClientCounter++;
                            }
                        }
                    }
                }
                finally
                {
                    Monitor.Exit(UClientObjects.SyncRoot);
                }

                // Sort array ascending
                ClientPortArray.Sort();

                if (ClientPortArray.Count != 0)
                {
#if DEBUGMODE
                    if (TSrvSetting.DL >= 4)
                    {
                        Console.WriteLine("FindFreeRemotingPort: ClientPortArray:");

                        for (PortCounter = 0; PortCounter <= ClientPortArray.Count - 1; PortCounter += 1)
                        {
                            Console.WriteLine(
                                "  Index: " + PortCounter.ToString() + "; Value: " + ClientPortArray[PortCounter].ToString() + Environment.NewLine);
                        }
                    }
#endif

                    if ((short)ClientPortArray[0] > LowestAvailableRemotingPort)
                    {
                        // first entry has an higher Port than the lowest available Port > assign lowest available Port
                        FreeRemotingPort = LowestAvailableRemotingPort;

                        // $IFDEF DEBUGMODE Console.WriteLine('FindFreeRemotingPort: First entry has an higher Port than the lowest available Port > assign lowest available Port');$ENDIF
                    }
                    else
                    {
                        // check for an 'hole' in the Port numbers to find a free Port number
                        for (PortCounter = 0; PortCounter <= ClientPortArray.Count - 1; PortCounter += 1)
                        {
                            PotentiallyFreeRemotingPort = (short)((short)ClientPortArray[PortCounter] + 1);

                            // $IFDEF DEBUGMODE Console.WriteLine('FindFreeRemotingPort: ' + PotentiallyFreeRemotingPort.ToString + ' is a potentially free Port');$ENDIF
                            if (PortCounter != (ClientPortArray.Count - 1))
                            {
                                if ((short)ClientPortArray[PortCounter + 1] != PotentiallyFreeRemotingPort)
                                {
                                    // there is a gap in the list of Port numbers > use this for the free Port
                                    FreeRemotingPort = PotentiallyFreeRemotingPort;

                                    // $IFDEF DEBUGMODE Console.WriteLine('FindFreeRemotingPort: there is a gap in the list of Port numbers > use Port ' + FreeRemotingPort.ToString + ' as free Port');$ENDIF
                                    break;
                                }
                            }
                            else
                            {
                                // this is the last entry in the list of Port numbers
                                FreeRemotingPort = PotentiallyFreeRemotingPort;

                                // $IFDEF DEBUGMODE Console.WriteLine('FindFreeRemotingPort: this is the last entry in the list of Port numbers > use Port ' + FreeRemotingPort.ToString + ' as free Port');$ENDIF
                            }
                        }
                    }
                }
                else
                {
                    // there were no entries in UClientObjects > assign lowest available Port number
                    FreeRemotingPort = LowestAvailableRemotingPort;

                    // $IFDEF DEBUGMODE Console.WriteLine('FindFreeRemotingPort: there were no entries in UClientObjects > assign lowest available Port number');$ENDIF
                }
            }
            else
            {
                // there were no entries in ClientPortArray > assign lowest available Port number
                FreeRemotingPort = LowestAvailableRemotingPort;
#if DEBUGMODE
                if (TSrvSetting.DL >= 4)
                {
                    Console.WriteLine("FindFreeRemotingPort: there were no entries in ClientPortArray -> assign lowest available Port number");
                }
#endif
            }

#if DEBUGMODE
            if (TSrvSetting.DL >= 4)
            {
                Console.WriteLine("FindFreeRemotingPort: Port number " + FreeRemotingPort.ToString() + " is free.");
            }
#endif
            return FreeRemotingPort;
        }

        /// <summary>
        /// Can be called by a Client to get memory information from the
        /// GarbageCollection on the Server.
        ///
        /// @comment For debugging/memory tracking purposes only.
        ///
        /// </summary>
        /// <returns>Result of a call to GC.GetTotalMemory.
        /// </returns>
        public System.Int32 GCGetApproxMemory()
        {
            System.Int32 ReturnValue;
            ReturnValue = (int)GC.GetTotalMemory(false);
            Console.WriteLine("TClientManager.GCGetApproxMemory: Approx. memory in use: " + ReturnValue.ToString());
            return ReturnValue;
        }

        /// <summary>
        /// Can be called by a Client to request information about the GarbageCollection
        /// Generation of a certain remoted object.
        ///
        /// @comment For debugging/memory tracking purposes only.
        ///
        /// </summary>
        /// <param name="AObject">Remoted Object</param>
        /// <returns>GC Generation (or -1 if the object no longer exists)
        /// </returns>
        public System.Int32 GCGetGCGeneration(object AObject)
        {
            System.Int32 ReturnValue;
            try
            {
                ReturnValue = GC.GetGeneration(AObject);
            }
            catch (Exception)
            {
                ReturnValue = -1;
                Console.WriteLine("TClientManager.GetGCGeneration: Object no longer in memory!");
            }
            return ReturnValue;
        }

        /// <summary>
        /// Can be called by a Client to perform a GarbageCollection on the Server.
        ///
        /// @comment For debugging/memory tracking purposes only.
        ///
        /// </summary>
        /// <returns>Result of a call to GC.GetTotalMemory after the GC was performed.
        /// </returns>
        public System.Int32 GCPerformGC()
        {
            GC.Collect();
            Console.WriteLine("TClientManager.PerformGC: GC performed");
            return (int)GC.GetTotalMemory(false);
        }

        #endregion
    }

    /// <summary>
    /// TAppDomainStatus and TRunningAppDomain have been moved from the implementation to the interface section, due to problems with DelphiCodeToDoc Enum with statuses that a AppDomain can have
    /// This class holds details about a connected Client.
    /// It is stored as an entry in the UClientObjects HashTable (one entry for each
    /// current Client connection).
    ///
    /// </summary>
    public class TRunningAppDomain : object
    {
        /// <summary> todoComment </summary>
        public System.Int32 FClientID;

        /// <summary> todoComment </summary>
        public String FUserID;

        /// <summary> todoComment </summary>
        public String FClientName;

        /// <summary> todoComment </summary>
        public DateTime FClientConnectionStartTime;

        /// <summary> todoComment </summary>
        public DateTime FClientConnectionFinishedTime;

        /// <summary> todoComment </summary>
        public DateTime FClientDisconnectionStartTime;

        /// <summary> todoComment </summary>
        public DateTime FClientDisconnectionFinishedTime;

        /// <summary> todoComment </summary>
        public String FClientComputerName;

        /// <summary> todoComment </summary>
        public String FClientIPAddress;

        /// <summary> todoComment </summary>
        public TClientServerConnectionType FClientServerConnectionType;

        /// <summary> todoComment </summary>
        public String FAppDomainName;

        /// <summary> todoComment </summary>
        public String FAppDomainRemotedObjectURL;

        /// <summary> todoComment </summary>
        public Int32 FAppDomainRemotingPort;

        /// <summary> todoComment </summary>
        public TClientAppDomainConnection FClientAppDomainConnection;
        private System.Object FDisconnectClientMonitor;
        private Boolean FClientDisconnectionScheduled;

        /// <summary> todoComment </summary>
        public TAppDomainStatus FAppDomainStatus;

        /// <summary>Serverassigned ID of the Client</summary>
        public System.Int32 ClientID
        {
            get
            {
                return FClientID;
            }
        }

        /// <summary>UserID for which the AppDomain was created</summary>
        public String UserID
        {
            get
            {
                return FUserID;
            }
        }

        /// <summary>Serverassigned name of the Client</summary>
        public String ClientName
        {
            get
            {
                return FClientName;
            }
        }

        /// <summary>Time when the Client connected to the Server.</summary>
        public DateTime ClientConnectionTime
        {
            get
            {
                return FClientConnectionStartTime;
            }
        }

        /// <summary>Computer name of the Client</summary>
        public String ClientComputerName
        {
            get
            {
                return FClientComputerName;
            }
        }

        /// <summary>IP Address of the Client</summary>
        public String ClientIPAddress
        {
            get
            {
                return FClientIPAddress;
            }
        }

        /// <summary>Type of the connection (eg. LAN, Remote)</summary>
        public TClientServerConnectionType ClientServerConnectionType
        {
            get
            {
                return FClientServerConnectionType;
            }
        }

        /// <summary>Serverassigned name of the Client AppDomain</summary>
        public String AppDomainName
        {
            get
            {
                return FAppDomainName;
            }

            set
            {
                FAppDomainName = value;
            }
        }

        /// <summary>.NET Remoting URL of a Test object (for testing purposes only)</summary>
        public String AppDomainRemotedObjectURL
        {
            get
            {
                return FAppDomainRemotedObjectURL;
            }

            set
            {
                FAppDomainRemotedObjectURL = value;
            }
        }

        /// <summary>.NET Remoting Port on which the Server will remote objects for the Client</summary>
        public Int32 AppDomainRemotingPort
        {
            get
            {
                return FAppDomainRemotingPort;
            }

            set
            {
                FAppDomainRemotingPort = value;
            }
        }

        /// <summary>Connection object to the Client's AppDomain</summary>
        public TClientAppDomainConnection ClientAppDomainConnection
        {
            get
            {
                return FClientAppDomainConnection;
            }

            set
            {
                FClientAppDomainConnection = value;
            }
        }

        /// <summary>Status that the Client AppDomain has</summary>
        public TAppDomainStatus AppDomainStatus
        {
            get
            {
                return FAppDomainStatus;
            }

            set
            {
                FAppDomainStatus = value;
            }
        }

        /// <summary>
        /// todoComment
        /// </summary>
        public System.Object DisconnectClientMonitor
        {
            get
            {
                return FDisconnectClientMonitor;
            }
        }

        /// <summary>
        /// todoComment
        /// </summary>
        public Boolean ClientDisconnectionScheduled
        {
            get
            {
                return FClientDisconnectionScheduled;
            }

            set
            {
                FClientDisconnectionScheduled = value;
            }
        }


        #region TRunningAppDomain

        /// <summary>
        /// Initialises fields.
        ///
        /// </summary>
        /// <param name="AClientID">Server-assigned ID of the Client</param>
        /// <param name="AUserID">AUserID for which the AppDomain was created</param>
        /// <param name="AClientName">Server-assigned name of the Client</param>
        /// <param name="AClientComputerName">Computer name of the Client</param>
        /// <param name="AClientIPAddress">IP Address of the Client</param>
        /// <param name="AClientServerConnectionType">Type of the connection (eg. LAN, Remote)</param>
        /// <param name="AAppDomainName">Server-assigned name of the Client AppDomain
        /// </param>
        /// <returns>void</returns>
        public TRunningAppDomain(System.Int32 AClientID,
            String AUserID,
            String AClientName,
            String AClientComputerName,
            String AClientIPAddress,
            TClientServerConnectionType AClientServerConnectionType,
            String AAppDomainName)
        {
            FClientID = AClientID;
            FUserID = AUserID;
            FClientName = AClientName;
            FClientComputerName = AClientComputerName;
            FClientIPAddress = AClientIPAddress;
            FClientServerConnectionType = AClientServerConnectionType;
            FClientConnectionStartTime = DateTime.Now;
            FAppDomainName = AAppDomainName;
            FAppDomainStatus = TAppDomainStatus.adsConnectingLoginVerification;
            FDisconnectClientMonitor = new System.Object();
        }

        /// <summary>
        /// Comment
        ///
        /// </summary>
        /// <param name="AAppDomainRemotedObjectURL">.NET Remoting URL of a Test object (for
        /// testing purposes only)</param>
        /// <param name="AAppDomainRemotingPort"></param>
        /// <param name="AClientAppDomainConnection">Object that allows a connection to the
        /// Client's AppDomain without causing a load of the Assemblies that are
        /// loaded in the Client's AppDomain into the Default AppDomain.
        /// </param>
        /// <returns>void</returns>
        public void PassInClientRemotingInfo(String AAppDomainRemotedObjectURL,
            Int32 AAppDomainRemotingPort,
            TClientAppDomainConnection AClientAppDomainConnection)
        {
            FAppDomainRemotedObjectURL = AAppDomainRemotedObjectURL;
            FAppDomainRemotingPort = AAppDomainRemotingPort;
            FClientAppDomainConnection = AClientAppDomainConnection;
        }

        #endregion
    }

    /// <summary>
    /// Class for threaded disconnection of Clients.
    ///
    /// </summary>
    public class TDisconnectClientThread
    {
        private TRunningAppDomain FAppDomainEntry;
        private Int32 FClientID;
        private String FReason;

        /// <summary>
        /// todoComment
        /// </summary>
        public TRunningAppDomain AppDomainEntry
        {
            get
            {
                return FAppDomainEntry;
            }

            set
            {
                FAppDomainEntry = value;
            }
        }

        /// <summary>
        /// todoComment
        /// </summary>
        public Int32 ClientID
        {
            get
            {
                return FClientID;
            }

            set
            {
                FClientID = value;
            }
        }

        /// <summary>
        /// todoComment
        /// </summary>
        public String Reason
        {
            get
            {
                return FReason;
            }

            set
            {
                FReason = value;
            }
        }

        #region TDisconnectClientThread

        /// <summary>
        /// todoComment
        /// </summary>
        public void StartClientDisconnection()
        {
            const Int32 UNLOAD_RETRIES = 20;
            Int32 UnloadExceptionCount;
            String ClientInfo;
            Boolean DBConnectionAlreadyClosed;
            Boolean UnloadFinished;

            DBConnectionAlreadyClosed = false;
            UnloadExceptionCount = 0;
            try
            {
                ClientInfo = "'" + FAppDomainEntry.FClientName + "' (ClientID: " + FAppDomainEntry.FClientID.ToString() + ")";
#if DEBUGMODE
                if (TSrvSetting.DL >= 4)
                {
                    Console.WriteLine(TClientManager.FormatClientList(false));
                    Console.WriteLine(TClientManager.FormatClientList(true));
                }
#endif

                if (TSrvSetting.DL >= 4)
                {
                    TLogging.Log("Disconnecting Client " + ClientInfo + " (Reason: " + FReason + ")...",
                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                }
                else
                {
                    TLogging.Log("Disconnecting Client " + ClientInfo + " (Reason: " + FReason + ")...", TLoggingType.ToLogfile);
                }

                FAppDomainEntry.FClientDisconnectionStartTime = DateTime.Now;
                FAppDomainEntry.FAppDomainStatus = TAppDomainStatus.adsDisconnectingDBClosing;
                try
                {
                    if (TSrvSetting.DL >= 4)
                    {
                        TLogging.Log("Closing Client DB Connection... [Client " + ClientInfo + "]", TLoggingType.ToConsole | TLoggingType.ToLogfile);
                    }

                    try
                    {
                        FAppDomainEntry.ClientAppDomainConnection.CloseDBConnection();
                    }
                    catch (System.ArgumentNullException)
                    {
                        // don't do anything here: this Exception only indicates that the
                        // DB connection was already closed.
                        DBConnectionAlreadyClosed = true;
                    }
                    catch (Exception Exp)
                    {
                        // make sure any other Exception is going to be handled
                        throw Exp;
                    }

                    if (!DBConnectionAlreadyClosed)
                    {
                        if (TSrvSetting.DL >= 4)
                        {
                            TLogging.Log("Closed Client DB Connection. [Client " + ClientInfo + "]", TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        }
                    }
                    else
                    {
                        if (TSrvSetting.DL >= 4)
                        {
                            TLogging.Log("Client DB Connection was already closed. [Client " + ClientInfo + "]",
                                TLoggingType.ToConsole | TLoggingType.ToLogfile);
                        }
                    }
                }
                catch (Exception exp)
                {
                    TLogging.Log(
                        "Error closing Database connection on Client disconnection!" + " [Client " + ClientInfo + "]" + "Exception: " + exp.ToString());
                }

                // TODO 1 oChristianK cLogging (Console) : Put the following debug messages again in a DEBUGMODE conditional compilation directive; this was removed to trace problems in on live installations!
                if (TSrvSetting.DL >= 5)
                {
                    TLogging.Log("  Before calling ClientAppDomainConnection.StopClientAppDomain...  [Client " + ClientInfo + ']',
                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                }

                FAppDomainEntry.ClientAppDomainConnection.StopClientAppDomain();

                if (TSrvSetting.DL >= 5)
                {
                    TLogging.Log("  After calling ClientAppDomainConnection.StopClientAppDomain...  [Client " + ClientInfo + ']',
                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                }

                FAppDomainEntry.FAppDomainStatus = TAppDomainStatus.adsDisconnectingAppDomainUnloading;
                UnloadFinished = false;

                while (!UnloadFinished)
                {
                    try
                    {
Retry:                          //             used only for repeating Unload when an Exception happened

                        if (Monitor.TryEnter(ClientManager.UAppDomainUnloadMonitor, 250))
                        {
                            /*
                             * Try to unload the AppDomain. If an Exception occurs, retries are made
                             * after a delay - until a maximum of retries is reached.
                             */
#if DEBUGMODE
                            if (TSrvSetting.DL >= 5)
                            {
                                Console.WriteLine(
                                    "  Unloading AppDomain '" + FAppDomainEntry.ClientAppDomainConnection.AppDomainName + "' [Client " + ClientInfo +
                                    "]");
                            }
#endif
                            try
                            {
                                if (TSrvSetting.DL >= 4)
                                {
                                    TLogging.Log("Unloading Client Session (AppDomain)..." + "' [Client " + ClientInfo + "]",
                                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                }

                                // Note for developers: uncomment the following code to see that the retrying of Unload really works in case Exceptions are thrown,,,
                                // if UnloadExceptionCount < 4 then
                                // begin
                                // raise Exception.Create();
                                // end;
                                // Unload the AppDomain
                                FAppDomainEntry.ClientAppDomainConnection.Unload();

                                // Everything went fine!
                                if (TSrvSetting.DL >= 4)
                                {
                                    TLogging.Log("Unloaded Client Session (AppDomain)." + "' [Client " + ClientInfo + "]",
                                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                }

                                UnloadExceptionCount = 0;
                                FAppDomainEntry.FClientAppDomainConnection = null;
                                FAppDomainEntry.FClientDisconnectionFinishedTime = DateTime.Now;
                                FAppDomainEntry.FAppDomainStatus = TAppDomainStatus.adsStopped;
#if DEBUGMODE
                                if (TSrvSetting.DL >= 5)
                                {
                                    Console.WriteLine("  AppDomain unloaded [Client " + ClientInfo + "]");
                                }
#endif
                            }
                            catch (Exception UnloadException)
                            {
                                // Something went wrong during Unload; log this, wait a bit and
                                // try again!
                                UnloadExceptionCount = UnloadExceptionCount + 1;
                                TLogging.Log(
                                    "Error unloading Client Session (AppDomain) [Client " + ClientInfo + "] on Client disconnection   (try #" +
                                    UnloadExceptionCount.ToString() + ")!  " + "Exception: " + UnloadException.ToString());
                                Monitor.PulseAll(ClientManager.UAppDomainUnloadMonitor);
                                Monitor.Exit(ClientManager.UAppDomainUnloadMonitor);
                                Thread.Sleep(UnloadExceptionCount * 1000);
                            }

                            if ((UnloadExceptionCount != 0) && (UnloadExceptionCount < UNLOAD_RETRIES))
                            {
                                goto Retry;
                            }

#if DEBUGMODE
                            if (TSrvSetting.DL >= 5)
                            {
                                Console.WriteLine("  AppDomain unloading finished [Client " + ClientInfo + ']');
                            }
#endif

                            // Logging: was Unload successful?
                            if (FAppDomainEntry.FAppDomainStatus == TAppDomainStatus.adsStopped)
                            {
                                if (TSrvSetting.DL >= 4)
                                {
                                    TLogging.Log(
                                        "Client " + ClientInfo + " has been disconnected (took " +
                                        FAppDomainEntry.FClientDisconnectionFinishedTime.Subtract(
                                            FAppDomainEntry.FClientDisconnectionStartTime).TotalSeconds.ToString() + " sec).",
                                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                }
                                else
                                {
                                    TLogging.Log("Client " + ClientInfo + " has been disconnected.", TLoggingType.ToLogfile);
                                }
                            }
                            else
                            {
                                if (TSrvSetting.DL >= 4)
                                {
                                    TLogging.Log("Client " + ClientInfo + " could not be disconnected!",
                                        TLoggingType.ToConsole | TLoggingType.ToLogfile);
                                }
                                else
                                {
                                    TLogging.Log("Client " + ClientInfo + " could not be disconnected!", TLoggingType.ToLogfile);
                                }
                            }

                            UnloadFinished = true;

                            // Notify any other waiting Threads that they can proceed with the
                            // Unloading of their AppDomain
                            try
                            {
                                Monitor.PulseAll(ClientManager.UAppDomainUnloadMonitor);
                            }
                            catch (System.Threading.SynchronizationLockException Exp)
                            {
                                if (UnloadExceptionCount == UNLOAD_RETRIES)
                                {
                                }
                                // ignore this Exception if the amount of retries is reached
                                // the Monitor is already Exited in this case
                                else
                                {
                                    throw Exp;
                                }
                            }
                            catch (Exception Exp)
                            {
                                throw Exp;
                            }
                        }
                        else
                        {
                            if (TSrvSetting.DL >= 4)
                            {
                                TLogging.Log(
                                    "Client disconnection Thread is blocked by another client disconnection thread. Waiting a bit... (ClientID: " +
                                    AppDomainEntry.FClientID.ToString() + ")...",
                                    TLoggingType.ToConsole | TLoggingType.ToLogfile);
                            }

                            Thread.Sleep(2000);

                            if (TSrvSetting.DL >= 4)
                            {
                                TLogging.Log(
                                    "Trying to continue Client disconnection after Thread was blocked by another client disconnection thread..." +
                                    "' (ClientID: " + AppDomainEntry.FClientID.ToString() + ")...",
                                    TLoggingType.ToConsole | TLoggingType.ToLogfile);
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(ClientManager.UAppDomainUnloadMonitor);
                    }
                }
            }
            catch (Exception Exp)
            {
                TLogging.Log(
                    "StartClientDisconnection for ClientID: " + FClientID.ToString() + ": Exception occured: " + Exp.ToString(),
                    TLoggingType.ToConsole |
                    TLoggingType.ToLogfile);
            }
        }

        #endregion
    }
}