Imports System.Windows.Forms
Imports System.Xml
Imports System.Globalization
Imports mRemoteNG.App.Runtime
Imports System.Data
Imports System.Data.SqlClient
Imports System.IO
Imports mRemoteNG.My
Imports PSTaskDialog

Namespace Config
    Namespace Connections
        Public Class Load
#Region "Private Properties"
            Private xDom As XmlDocument
            Private confVersion As Double
            Private pW As String = App.Info.General.EncryptionKey

            Private sqlCon As SqlConnection
            Private sqlQuery As SqlCommand
            Private sqlRd As SqlDataReader

            Private _selectedTreeNode As TreeNode
#End Region

#Region "Public Properties"
            Private _UseSQL As Boolean
            Public Property UseSQL() As Boolean
                Get
                    Return _UseSQL
                End Get
                Set(ByVal value As Boolean)
                    _UseSQL = value
                End Set
            End Property

            Private _SQLHost As String
            Public Property SQLHost() As String
                Get
                    Return _SQLHost
                End Get
                Set(ByVal value As String)
                    _SQLHost = value
                End Set
            End Property

            Private _SQLDatabaseName As String
            Public Property SQLDatabaseName() As String
                Get
                    Return _SQLDatabaseName
                End Get
                Set(ByVal value As String)
                    _SQLDatabaseName = value
                End Set
            End Property

            Private _SQLUsername As String
            Public Property SQLUsername() As String
                Get
                    Return _SQLUsername
                End Get
                Set(ByVal value As String)
                    _SQLUsername = value
                End Set
            End Property

            Private _SQLPassword As String
            Public Property SQLPassword() As String
                Get
                    Return _SQLPassword
                End Get
                Set(ByVal value As String)
                    _SQLPassword = value
                End Set
            End Property

            Private _SQLUpdate As Boolean
            Public Property SQLUpdate() As Boolean
                Get
                    Return _SQLUpdate
                End Get
                Set(ByVal value As Boolean)
                    _SQLUpdate = value
                End Set
            End Property

            Private _PreviousSelected As String
            Public Property PreviousSelected() As String
                Get
                    Return _PreviousSelected
                End Get
                Set(ByVal value As String)
                    _PreviousSelected = value
                End Set
            End Property

            Private _ConnectionFileName As String
            Public Property ConnectionFileName() As String
                Get
                    Return Me._ConnectionFileName
                End Get
                Set(ByVal value As String)
                    Me._ConnectionFileName = value
                End Set
            End Property

            Public Property RootTreeNode() As TreeNode

            Public Property ConnectionList() As Connection.List

            Private _ContainerList As Container.List
            Public Property ContainerList() As Container.List
                Get
                    Return Me._ContainerList
                End Get
                Set(ByVal value As Container.List)
                    Me._ContainerList = value
                End Set
            End Property

            Private _PreviousConnectionList As Connection.List
            Public Property PreviousConnectionList() As Connection.List
                Get
                    Return _PreviousConnectionList
                End Get
                Set(ByVal value As Connection.List)
                    _PreviousConnectionList = value
                End Set
            End Property

            Private _PreviousContainerList As Container.List
            Public Property PreviousContainerList() As Container.List
                Get
                    Return _PreviousContainerList
                End Get
                Set(ByVal value As Container.List)
                    _PreviousContainerList = value
                End Set
            End Property
#End Region

#Region "Public Methods"
            Public Sub Load(ByVal import As Boolean)
                If UseSQL Then
                    LoadFromSQL()
                Else
                    Dim connections As String = DecryptCompleteFile()
                    LoadFromXML(connections, import)
                End If

                frmMain.UsingSqlServer = UseSQL
                frmMain.ConnectionsFileName = ConnectionFileName

                If Not import Then Putty.Sessions.AddSessionsToTree()
            End Sub
#End Region

#Region "SQL"
            Private Delegate Sub LoadFromSqlDelegate()
            Private Sub LoadFromSQL()
                If Windows.treeForm Is Nothing OrElse Windows.treeForm.tvConnections Is Nothing Then Return
                If Windows.treeForm.tvConnections.InvokeRequired Then
                    Windows.treeForm.tvConnections.Invoke(New LoadFromSqlDelegate(AddressOf LoadFromSQL))
                    Return
                End If

                Try
                    IsConnectionsFileLoaded = False

                    If _SQLUsername <> "" Then
                        sqlCon = New SqlConnection("Data Source=" & _SQLHost & ";Initial Catalog=" & _SQLDatabaseName & ";User Id=" & _SQLUsername & ";Password=" & _SQLPassword)
                    Else
                        sqlCon = New SqlConnection("Data Source=" & _SQLHost & ";Initial Catalog=" & _SQLDatabaseName & ";Integrated Security=True")
                    End If

                    sqlCon.Open()

                    sqlQuery = New SqlCommand("SELECT * FROM tblRoot", sqlCon)
                    sqlRd = sqlQuery.ExecuteReader(CommandBehavior.CloseConnection)

                    sqlRd.Read()

                    If sqlRd.HasRows = False Then
                        SaveConnections()

                        sqlQuery = New SqlCommand("SELECT * FROM tblRoot", sqlCon)
                        sqlRd = sqlQuery.ExecuteReader(CommandBehavior.CloseConnection)

                        sqlRd.Read()
                    End If

                    confVersion = Convert.ToDouble(sqlRd.Item("confVersion"), CultureInfo.InvariantCulture)
                    Const maxSupportedSchemaVersion As Double = 2.5
                    If confVersion > maxSupportedSchemaVersion Then
                        cTaskDialog.ShowTaskDialogBox(frmMain, Application.ProductName, "Incompatible database schema", String.Format("The database schema on the server is not supported. Please upgrade to a newer version of {0}.", Application.ProductName), String.Format("Schema Version: {1}{0}Highest Supported Version: {2}", vbNewLine, confVersion.ToString(), maxSupportedSchemaVersion.ToString()), "", "", "", "", eTaskDialogButtons.OK, eSysIcons.Error, Nothing)
                        Throw New Exception(String.Format("Incompatible database schema (schema version {0}).", confVersion))
                    End If

                    RootTreeNode.Name = sqlRd.Item("Name")

                    Dim rootInfo As New Root.Info(Root.Info.RootType.Connection)
                    rootInfo.Name = RootTreeNode.Name
                    rootInfo.TreeNode = RootTreeNode

                    RootTreeNode.Tag = rootInfo
                    RootTreeNode.ImageIndex = Images.Enums.TreeImage.Root
                    RootTreeNode.SelectedImageIndex = Images.Enums.TreeImage.Root

                    If Security.Crypt.Decrypt(sqlRd.Item("Protected"), pW) <> "ThisIsNotProtected" Then
                        If Authenticate(sqlRd.Item("Protected"), False, rootInfo) = False Then
                            My.Settings.LoadConsFromCustomLocation = False
                            My.Settings.CustomConsPath = ""
                            RootTreeNode.Remove()
                            Exit Sub
                        End If
                    End If

                    sqlRd.Close()

                    Windows.treeForm.tvConnections.BeginUpdate()

                    ' SECTION 3. Populate the TreeView with the DOM nodes.
                    AddNodesFromSQL(RootTreeNode)

                    RootTreeNode.Expand()

                    'expand containers
                    For Each contI As Container.Info In Me._ContainerList
                        If contI.IsExpanded = True Then
                            contI.TreeNode.Expand()
                        End If
                    Next

                    Windows.treeForm.tvConnections.EndUpdate()

                    'open connections from last mremote session
                    If My.Settings.OpenConsFromLastSession = True And My.Settings.NoReconnect = False Then
                        For Each conI As Connection.Info In ConnectionList
                            If conI.PleaseConnect = True Then
                                OpenConnection(conI)
                            End If
                        Next
                    End If

                    IsConnectionsFileLoaded = True
                    Windows.treeForm.InitialRefresh()
                    SetSelectedNode(_selectedTreeNode)
                Catch ex As Exception
                    Throw
                Finally
                    If sqlCon IsNot Nothing Then
                        sqlCon.Close()
                    End If
                End Try
            End Sub

            Private Delegate Sub SetSelectedNodeDelegate(ByVal treeNode As TreeNode)
            Private Shared Sub SetSelectedNode(ByVal treeNode As TreeNode)
                If Tree.Node.TreeView IsNot Nothing AndAlso Tree.Node.TreeView.InvokeRequired Then
                    Windows.treeForm.Invoke(New SetSelectedNodeDelegate(AddressOf SetSelectedNode), New Object() {treeNode})
                    Return
                End If
                Windows.treeForm.tvConnections.SelectedNode = treeNode
            End Sub

            Private Sub AddNodesFromSQL(ByVal baseNode As TreeNode)
                Try
                    sqlCon.Open()
                    sqlQuery = New SqlCommand("SELECT * FROM tblCons ORDER BY PositionID ASC", sqlCon)
                    sqlRd = sqlQuery.ExecuteReader(CommandBehavior.CloseConnection)

                    If sqlRd.HasRows = False Then
                        Exit Sub
                    End If

                    Dim tNode As TreeNode

                    While sqlRd.Read
                        tNode = New TreeNode(sqlRd.Item("Name"))
                        'baseNode.Nodes.Add(tNode)

                        If Tree.Node.GetNodeTypeFromString(sqlRd.Item("Type")) = Tree.Node.Type.Connection Then
                            Dim conI As Connection.Info = GetConnectionInfoFromSQL()
                            conI.TreeNode = tNode
                            'conI.Parent = _previousContainer 'NEW

                            Me._ConnectionList.Add(conI)

                            tNode.Tag = conI

                            If SQLUpdate = True Then
                                Dim prevCon As Connection.Info = PreviousConnectionList.FindByConstantID(conI.ConstantID)

                                If prevCon IsNot Nothing Then
                                    For Each prot As Connection.Protocol.Base In prevCon.OpenConnections
                                        prot.InterfaceControl.Info = conI
                                        conI.OpenConnections.Add(prot)
                                    Next

                                    If conI.OpenConnections.Count > 0 Then
                                        tNode.ImageIndex = Images.Enums.TreeImage.ConnectionOpen
                                        tNode.SelectedImageIndex = Images.Enums.TreeImage.ConnectionOpen
                                    Else
                                        tNode.ImageIndex = Images.Enums.TreeImage.ConnectionClosed
                                        tNode.SelectedImageIndex = Images.Enums.TreeImage.ConnectionClosed
                                    End If
                                Else
                                    tNode.ImageIndex = Images.Enums.TreeImage.ConnectionClosed
                                    tNode.SelectedImageIndex = Images.Enums.TreeImage.ConnectionClosed
                                End If

                                If conI.ConstantID = _PreviousSelected Then
                                    _selectedTreeNode = tNode
                                End If
                            Else
                                tNode.ImageIndex = Images.Enums.TreeImage.ConnectionClosed
                                tNode.SelectedImageIndex = Images.Enums.TreeImage.ConnectionClosed
                            End If
                        ElseIf Tree.Node.GetNodeTypeFromString(sqlRd.Item("Type")) = Tree.Node.Type.Container Then
                            Dim contI As New Container.Info
                            'If tNode.Parent IsNot Nothing Then
                            '    If Tree.Node.GetNodeType(tNode.Parent) = Tree.Node.Type.Container Then
                            '        contI.Parent = tNode.Parent.Tag
                            '    End If
                            'End If
                            '_previousContainer = contI 'NEW
                            contI.TreeNode = tNode

                            contI.Name = sqlRd.Item("Name")

                            Dim conI As Connection.Info

                            conI = GetConnectionInfoFromSQL()

                            conI.Parent = contI
                            conI.IsContainer = True
                            contI.ConnectionInfo = conI

                            If SQLUpdate = True Then
                                Dim prevCont As Container.Info = PreviousContainerList.FindByConstantID(conI.ConstantID)
                                If prevCont IsNot Nothing Then
                                    contI.IsExpanded = prevCont.IsExpanded
                                End If

                                If conI.ConstantID = _PreviousSelected Then
                                    _selectedTreeNode = tNode
                                End If
                            Else
                                If sqlRd.Item("Expanded") = True Then
                                    contI.IsExpanded = True
                                Else
                                    contI.IsExpanded = False
                                End If
                            End If

                            Me._ContainerList.Add(contI)
                            Me._ConnectionList.Add(conI)

                            tNode.Tag = contI
                            tNode.ImageIndex = Images.Enums.TreeImage.Container
                            tNode.SelectedImageIndex = Images.Enums.TreeImage.Container
                        End If

                        Dim parentId As String = sqlRd.Item("ParentID").ToString().Trim()
                        If String.IsNullOrEmpty(parentId) Or parentId = "0" Then
                            baseNode.Nodes.Add(tNode)
                        Else
                            Dim pNode As TreeNode = Tree.Node.GetNodeFromConstantID(sqlRd.Item("ParentID"))

                            If pNode IsNot Nothing Then
                                pNode.Nodes.Add(tNode)

                                If Tree.Node.GetNodeType(tNode) = Tree.Node.Type.Connection Then
                                    TryCast(tNode.Tag, Connection.Info).Parent = pNode.Tag
                                ElseIf Tree.Node.GetNodeType(tNode) = Tree.Node.Type.Container Then
                                    TryCast(tNode.Tag, Container.Info).Parent = pNode.Tag
                                End If
                            Else
                                baseNode.Nodes.Add(tNode)
                            End If
                        End If

                        'AddNodesFromSQL(tNode)
                    End While
                Catch ex As Exception
                    MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, My.Language.strAddNodesFromSqlFailed & vbNewLine & ex.Message, True)
                End Try
            End Sub

            Private Function GetConnectionInfoFromSQL() As Connection.Info
                Try
                    Dim conI As New Connection.Info

                    With sqlRd
                        conI.PositionID = .Item("PositionID")
                        conI.ConstantID = .Item("ConstantID")
                        conI.Name = .Item("Name")
                        conI.Description = .Item("Description")
                        conI.Hostname = .Item("Hostname")
                        conI.Username = .Item("Username")
                        conI.Password = Security.Crypt.Decrypt(.Item("Password"), pW)
                        conI.Domain = .Item("DomainName")
                        conI.DisplayWallpaper = .Item("DisplayWallpaper")
                        conI.DisplayThemes = .Item("DisplayThemes")
                        conI.CacheBitmaps = .Item("CacheBitmaps")
                        conI.UseConsoleSession = .Item("ConnectToConsole")

                        conI.RedirectDiskDrives = .Item("RedirectDiskDrives")
                        conI.RedirectPrinters = .Item("RedirectPrinters")
                        conI.RedirectPorts = .Item("RedirectPorts")
                        conI.RedirectSmartCards = .Item("RedirectSmartCards")
                        conI.RedirectKeys = .Item("RedirectKeys")
                        conI.RedirectSound = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.RDPSounds), .Item("RedirectSound"))

                        conI.Protocol = Tools.Misc.StringToEnum(GetType(Connection.Protocol.Protocols), .Item("Protocol"))
                        conI.Port = .Item("Port")
                        conI.PuttySession = .Item("PuttySession")

                        conI.Colors = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.RDPColors), .Item("Colors"))
                        conI.Resolution = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.RDPResolutions), .Item("Resolution"))

                        conI.Inherit = New Connection.Info.Inheritance(conI)
                        conI.Inherit.CacheBitmaps = .Item("InheritCacheBitmaps")
                        conI.Inherit.Colors = .Item("InheritColors")
                        conI.Inherit.Description = .Item("InheritDescription")
                        conI.Inherit.DisplayThemes = .Item("InheritDisplayThemes")
                        conI.Inherit.DisplayWallpaper = .Item("InheritDisplayWallpaper")
                        conI.Inherit.Domain = .Item("InheritDomain")
                        conI.Inherit.Icon = .Item("InheritIcon")
                        conI.Inherit.Panel = .Item("InheritPanel")
                        conI.Inherit.Password = .Item("InheritPassword")
                        conI.Inherit.Port = .Item("InheritPort")
                        conI.Inherit.Protocol = .Item("InheritProtocol")
                        conI.Inherit.PuttySession = .Item("InheritPuttySession")
                        conI.Inherit.RedirectDiskDrives = .Item("InheritRedirectDiskDrives")
                        conI.Inherit.RedirectKeys = .Item("InheritRedirectKeys")
                        conI.Inherit.RedirectPorts = .Item("InheritRedirectPorts")
                        conI.Inherit.RedirectPrinters = .Item("InheritRedirectPrinters")
                        conI.Inherit.RedirectSmartCards = .Item("InheritRedirectSmartCards")
                        conI.Inherit.RedirectSound = .Item("InheritRedirectSound")
                        conI.Inherit.Resolution = .Item("InheritResolution")
                        conI.Inherit.UseConsoleSession = .Item("InheritUseConsoleSession")
                        conI.Inherit.Username = .Item("InheritUsername")

                        conI.Icon = .Item("Icon")
                        conI.Panel = .Item("Panel")

                        If Me.confVersion > 1.5 Then '1.6
                            conI.PreExtApp = .Item("PreExtApp")
                            conI.PostExtApp = .Item("PostExtApp")
                            conI.Inherit.PreExtApp = .Item("InheritPreExtApp")
                            conI.Inherit.PostExtApp = .Item("InheritPostExtApp")
                        End If

                        If Me.confVersion > 1.6 Then '1.7
                            conI.VNCCompression = Tools.Misc.StringToEnum(GetType(Connection.Protocol.VNC.Compression), .Item("VNCCompression"))
                            conI.VNCEncoding = Tools.Misc.StringToEnum(GetType(Connection.Protocol.VNC.Encoding), .Item("VNCEncoding"))
                            conI.VNCAuthMode = Tools.Misc.StringToEnum(GetType(Connection.Protocol.VNC.AuthMode), .Item("VNCAuthMode"))
                            conI.VNCProxyType = Tools.Misc.StringToEnum(GetType(Connection.Protocol.VNC.ProxyType), .Item("VNCProxyType"))
                            conI.VNCProxyIP = .Item("VNCProxyIP")
                            conI.VNCProxyPort = .Item("VNCProxyPort")
                            conI.VNCProxyUsername = .Item("VNCProxyUsername")
                            conI.VNCProxyPassword = Security.Crypt.Decrypt(.Item("VNCProxyPassword"), pW)
                            conI.VNCColors = Tools.Misc.StringToEnum(GetType(Connection.Protocol.VNC.Colors), .Item("VNCColors"))
                            conI.VNCSmartSizeMode = Tools.Misc.StringToEnum(GetType(Connection.Protocol.VNC.SmartSizeMode), .Item("VNCSmartSizeMode"))
                            conI.VNCViewOnly = .Item("VNCViewOnly")

                            conI.Inherit.VNCCompression = .Item("InheritVNCCompression")
                            conI.Inherit.VNCEncoding = .Item("InheritVNCEncoding")
                            conI.Inherit.VNCAuthMode = .Item("InheritVNCAuthMode")
                            conI.Inherit.VNCProxyType = .Item("InheritVNCProxyType")
                            conI.Inherit.VNCProxyIP = .Item("InheritVNCProxyIP")
                            conI.Inherit.VNCProxyPort = .Item("InheritVNCProxyPort")
                            conI.Inherit.VNCProxyUsername = .Item("InheritVNCProxyUsername")
                            conI.Inherit.VNCProxyPassword = .Item("InheritVNCProxyPassword")
                            conI.Inherit.VNCColors = .Item("InheritVNCColors")
                            conI.Inherit.VNCSmartSizeMode = .Item("InheritVNCSmartSizeMode")
                            conI.Inherit.VNCViewOnly = .Item("InheritVNCViewOnly")
                        End If

                        If Me.confVersion > 1.7 Then '1.8
                            conI.RDPAuthenticationLevel = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.AuthenticationLevel), .Item("RDPAuthenticationLevel"))

                            conI.Inherit.RDPAuthenticationLevel = .Item("InheritRDPAuthenticationLevel")
                        End If

                        If Me.confVersion > 1.8 Then '1.9
                            conI.RenderingEngine = Tools.Misc.StringToEnum(GetType(Connection.Protocol.HTTPBase.RenderingEngine), .Item("RenderingEngine"))
                            conI.MacAddress = .Item("MacAddress")

                            conI.Inherit.RenderingEngine = .Item("InheritRenderingEngine")
                            conI.Inherit.MacAddress = .Item("InheritMacAddress")
                        End If

                        If Me.confVersion > 1.9 Then '2.0
                            conI.UserField = .Item("UserField")

                            conI.Inherit.UserField = .Item("InheritUserField")
                        End If

                        If Me.confVersion > 2.0 Then '2.1
                            conI.ExtApp = .Item("ExtApp")

                            conI.Inherit.ExtApp = .Item("InheritExtApp")
                        End If

                        If Me.confVersion >= 2.2 Then
                            conI.RDGatewayUsageMethod = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.RDP.RDGatewayUsageMethod), .Item("RDGatewayUsageMethod"))
                            conI.RDGatewayHostname = .Item("RDGatewayHostname")
                            conI.RDGatewayUseConnectionCredentials = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.RDP.RDGatewayUseConnectionCredentials), .Item("RDGatewayUseConnectionCredentials"))
                            conI.RDGatewayUsername = .Item("RDGatewayUsername")
                            conI.RDGatewayPassword = Security.Crypt.Decrypt(.Item("RDGatewayPassword"), pW)
                            conI.RDGatewayDomain = .Item("RDGatewayDomain")
                            conI.Inherit.RDGatewayUsageMethod = .Item("InheritRDGatewayUsageMethod")
                            conI.Inherit.RDGatewayHostname = .Item("InheritRDGatewayHostname")
                            conI.Inherit.RDGatewayUsername = .Item("InheritRDGatewayUsername")
                            conI.Inherit.RDGatewayPassword = .Item("InheritRDGatewayPassword")
                            conI.Inherit.RDGatewayDomain = .Item("InheritRDGatewayDomain")
                        End If

                        If Me.confVersion >= 2.3 Then
                            conI.EnableFontSmoothing = .Item("EnableFontSmoothing")
                            conI.EnableDesktopComposition = .Item("EnableDesktopComposition")
                            conI.Inherit.EnableFontSmoothing = .Item("InheritEnableFontSmoothing")
                            conI.Inherit.EnableDesktopComposition = .Item("InheritEnableDesktopComposition")
                        End If

                        If confVersion >= 2.4 Then
                            conI.UseCredSsp = .Item("UseCredSsp")
                            conI.Inherit.UseCredSsp = .Item("InheritUseCredSsp")
                        End If

                        If confVersion >= 2.5 Then
                            conI.LoadBalanceInfo = .Item("LoadBalanceInfo")
                            conI.AutomaticResize = .Item("AutomaticResize")
                            conI.Inherit.LoadBalanceInfo = .Item("InheritLoadBalanceInfo")
                            conI.Inherit.AutomaticResize = .Item("InheritAutomaticResize")
                        End If

                        If SQLUpdate = True Then
                            conI.PleaseConnect = .Item("Connected")
                        End If
                    End With

                    Return conI
                Catch ex As Exception
                    MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, My.Language.strGetConnectionInfoFromSqlFailed & vbNewLine & ex.Message, True)
                End Try

                Return Nothing
            End Function
#End Region

#Region "XML"
            Private Function DecryptCompleteFile() As String
                Dim sRd As New StreamReader(Me._ConnectionFileName)

                Dim strCons As String
                strCons = sRd.ReadToEnd
                sRd.Close()

                If strCons <> "" Then
                    Dim strDecr As String = ""
                    Dim notDecr As Boolean = True

                    If strCons.Contains("<?xml version=""1.0"" encoding=""utf-8""?>") Then
                        strDecr = strCons
                        Return strDecr
                    End If

                    Try
                        strDecr = Security.Crypt.Decrypt(strCons, pW)

                        If strDecr <> strCons Then
                            notDecr = False
                        Else
                            notDecr = True
                        End If
                    Catch ex As Exception
                        notDecr = True
                    End Try

                    If notDecr Then
                        If Authenticate(strCons, True) = True Then
                            strDecr = Security.Crypt.Decrypt(strCons, pW)
                            notDecr = False
                        Else
                            notDecr = True
                        End If

                        If notDecr = False Then
                            Return strDecr
                        End If
                    Else
                        Return strDecr
                    End If
                End If

                Return ""
            End Function

            Private Sub LoadFromXML(ByVal cons As String, ByVal import As Boolean)
                Try
                    If Not import Then IsConnectionsFileLoaded = False

                    ' SECTION 1. Create a DOM Document and load the XML data into it.
                    Me.xDom = New XmlDocument()
                    If cons <> "" Then
                        xDom.LoadXml(cons)
                    Else
                        xDom.Load(Me._ConnectionFileName)
                    End If

                    If xDom.DocumentElement.HasAttribute("ConfVersion") Then
                        Me.confVersion = Convert.ToDouble(xDom.DocumentElement.Attributes("ConfVersion").Value.Replace(",", "."), CultureInfo.InvariantCulture)
                    Else
                        MessageCollector.AddMessage(Messages.MessageClass.WarningMsg, My.Language.strOldConffile)
                    End If

                    Const maxSupportedConfVersion As Double = 2.5
                    If confVersion > maxSupportedConfVersion Then
                        cTaskDialog.ShowTaskDialogBox(frmMain, Application.ProductName, "Incompatible connection file format", String.Format("The format of this connection file is not supported. Please upgrade to a newer version of {0}.", Application.ProductName), String.Format("{1}{0}File Format Version: {2}{0}Highest Supported Version: {3}", vbNewLine, ConnectionFileName, confVersion.ToString(), maxSupportedConfVersion.ToString()), "", "", "", "", eTaskDialogButtons.OK, eSysIcons.Error, Nothing)
                        Throw New Exception(String.Format("Incompatible connection file format (file format version {0}).", confVersion))
                    End If

                    ' SECTION 2. Initialize the treeview control.
                    Dim rootInfo As Root.Info
                    If import Then
                        rootInfo = Nothing
                    Else
                        Dim rootNodeName As String = ""
                        If xDom.DocumentElement.HasAttribute("Name") Then rootNodeName = xDom.DocumentElement.Attributes("Name").Value.Trim()
                        If Not String.IsNullOrEmpty(rootNodeName) Then
                            RootTreeNode.Name = rootNodeName
                        Else
                            RootTreeNode.Name = xDom.DocumentElement.Name
                        End If
                        RootTreeNode.Text = RootTreeNode.Name

                        rootInfo = New Root.Info(Root.Info.RootType.Connection)
                        rootInfo.Name = RootTreeNode.Name
                        rootInfo.TreeNode = RootTreeNode

                        RootTreeNode.Tag = rootInfo
                    End If

                    If Me.confVersion > 1.3 Then '1.4
                        If Security.Crypt.Decrypt(xDom.DocumentElement.Attributes("Protected").Value, pW) <> "ThisIsNotProtected" Then
                            If Authenticate(xDom.DocumentElement.Attributes("Protected").Value, False, rootInfo) = False Then
                                My.Settings.LoadConsFromCustomLocation = False
                                My.Settings.CustomConsPath = ""
                                RootTreeNode.Remove()
                                Exit Sub
                            End If
                        End If
                    End If

                    Dim isExportFile As Boolean = False
                    If confVersion >= 1.0 Then
                        If xDom.DocumentElement.Attributes("Export").Value = True Then
                            isExportFile = True
                        End If
                    End If

                    If import And Not isExportFile Then
                        MessageCollector.AddMessage(Messages.MessageClass.InformationMsg, My.Language.strCannotImportNormalSessionFile)
                        Return
                    End If

                    If Not isExportFile Then
                        RootTreeNode.ImageIndex = Images.Enums.TreeImage.Root
                        RootTreeNode.SelectedImageIndex = Images.Enums.TreeImage.Root
                    End If

                    Windows.treeForm.tvConnections.BeginUpdate()

                    ' SECTION 3. Populate the TreeView with the DOM nodes.
                    AddNodeFromXml(xDom.DocumentElement, RootTreeNode)

                    RootTreeNode.Expand()

                    'expand containers
                    For Each contI As Container.Info In Me._ContainerList
                        If contI.IsExpanded = True Then
                            contI.TreeNode.Expand()
                        End If
                    Next

                    Windows.treeForm.tvConnections.EndUpdate()

                    'open connections from last mremote session
                    If My.Settings.OpenConsFromLastSession = True And My.Settings.NoReconnect = False Then
                        For Each conI As Connection.Info In _ConnectionList
                            If conI.PleaseConnect = True Then
                                OpenConnection(conI)
                            End If
                        Next
                    End If

                    RootTreeNode.EnsureVisible()

                    If Not import Then IsConnectionsFileLoaded = True
                    Windows.treeForm.InitialRefresh()
                    SetSelectedNode(RootTreeNode)
                Catch ex As Exception
                    App.Runtime.IsConnectionsFileLoaded = False
                    MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, My.Language.strLoadFromXmlFailed & vbNewLine & ex.Message & vbNewLine & ex.StackTrace, True)
                    Throw
                End Try
            End Sub

            Private _previousContainer As Container.Info
            Private Sub AddNodeFromXml(ByRef parentXmlNode As XmlNode, ByRef parentTreeNode As TreeNode)
                Try
                    ' Loop through the XML nodes until the leaf is reached.
                    ' Add the nodes to the TreeView during the looping process.
                    If parentXmlNode.HasChildNodes() Then
                        For Each xmlNode As XmlNode In parentXmlNode.ChildNodes
                            Dim treeNode As TreeNode = New TreeNode(xmlNode.Attributes("Name").Value)
                            parentTreeNode.Nodes.Add(treeNode)

                            If Tree.Node.GetNodeTypeFromString(xmlNode.Attributes("Type").Value) = Tree.Node.Type.Connection Then 'connection info
                                Dim connectionInfo As Connection.Info = GetConnectionInfoFromXml(xmlNode)
                                connectionInfo.TreeNode = treeNode
                                connectionInfo.Parent = _previousContainer 'NEW

                                ConnectionList.Add(connectionInfo)

                                treeNode.Tag = connectionInfo
                                treeNode.ImageIndex = Images.Enums.TreeImage.ConnectionClosed
                                treeNode.SelectedImageIndex = Images.Enums.TreeImage.ConnectionClosed
                            ElseIf Tree.Node.GetNodeTypeFromString(xmlNode.Attributes("Type").Value) = Tree.Node.Type.Container Then  'container info
                                Dim containerInfo As New Container.Info
                                If treeNode.Parent IsNot Nothing Then
                                    If Tree.Node.GetNodeType(treeNode.Parent) = Tree.Node.Type.Container Then
                                        containerInfo.Parent = treeNode.Parent.Tag
                                    End If
                                End If
                                _previousContainer = containerInfo 'NEW
                                containerInfo.TreeNode = treeNode

                                containerInfo.Name = xmlNode.Attributes("Name").Value

                                If confVersion >= 0.8 Then
                                    If xmlNode.Attributes("Expanded").Value = "True" Then
                                        containerInfo.IsExpanded = True
                                    Else
                                        containerInfo.IsExpanded = False
                                    End If
                                End If

                                Dim connectionInfo As Connection.Info
                                If confVersion >= 0.9 Then
                                    connectionInfo = GetConnectionInfoFromXml(xmlNode)
                                Else
                                    connectionInfo = New Connection.Info
                                End If

                                connectionInfo.Parent = containerInfo
                                connectionInfo.IsContainer = True
                                containerInfo.ConnectionInfo = connectionInfo

                                ContainerList.Add(containerInfo)

                                treeNode.Tag = containerInfo
                                treeNode.ImageIndex = Images.Enums.TreeImage.Container
                                treeNode.SelectedImageIndex = Images.Enums.TreeImage.Container
                            End If

                            AddNodeFromXml(xmlNode, treeNode)
                        Next
                    Else
                        Dim nodeName As String = ""
                        Dim nameAttribute As XmlAttribute = parentXmlNode.Attributes("Name")
                        If Not IsNothing(nameAttribute) Then nodeName = nameAttribute.Value.Trim()
                        If Not String.IsNullOrEmpty(nodeName) Then
                            parentTreeNode.Text = nodeName
                        Else
                            parentTreeNode.Text = parentXmlNode.Name
                        End If
                    End If
                Catch ex As Exception
                    MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, My.Language.strAddNodeFromXmlFailed & vbNewLine & ex.Message & ex.StackTrace, True)
                    Throw
                End Try
            End Sub

            Private Function GetConnectionInfoFromXml(ByVal xxNode As XmlNode) As Connection.Info
                Dim conI As New Connection.Info

                Try
                    With xxNode
                        If Me.confVersion > 0.1 Then '0.2
                            conI.Name = .Attributes("Name").Value
                            conI.Description = .Attributes("Descr").Value
                            conI.Hostname = .Attributes("Hostname").Value
                            conI.Username = .Attributes("Username").Value
                            conI.Password = Security.Crypt.Decrypt(.Attributes("Password").Value, pW)
                            conI.Domain = .Attributes("Domain").Value
                            conI.DisplayWallpaper = .Attributes("DisplayWallpaper").Value
                            conI.DisplayThemes = .Attributes("DisplayThemes").Value
                            conI.CacheBitmaps = .Attributes("CacheBitmaps").Value

                            If Me.confVersion < 1.1 Then '1.0 - 0.1
                                If .Attributes("Fullscreen").Value = True Then
                                    conI.Resolution = Connection.Protocol.RDP.RDPResolutions.Fullscreen
                                Else
                                    conI.Resolution = Connection.Protocol.RDP.RDPResolutions.FitToWindow
                                End If
                            End If
                        End If

                        If Me.confVersion > 0.2 Then '0.3
                            If Me.confVersion < 0.7 Then
                                If CType(.Attributes("UseVNC").Value, Boolean) = True Then
                                    conI.Protocol = Connection.Protocol.Protocols.VNC
                                    conI.Port = .Attributes("VNCPort").Value
                                Else
                                    conI.Protocol = Connection.Protocol.Protocols.RDP
                                End If
                            End If
                        Else
                            conI.Port = Connection.Protocol.RDP.Defaults.Port
                            conI.Protocol = Connection.Protocol.Protocols.RDP
                        End If

                        If Me.confVersion > 0.3 Then '0.4
                            If Me.confVersion < 0.7 Then
                                If CType(.Attributes("UseVNC").Value, Boolean) = True Then
                                    conI.Port = .Attributes("VNCPort").Value
                                Else
                                    conI.Port = .Attributes("RDPPort").Value
                                End If
                            End If

                            conI.UseConsoleSession = .Attributes("ConnectToConsole").Value
                        Else
                            If Me.confVersion < 0.7 Then
                                If CType(.Attributes("UseVNC").Value, Boolean) = True Then
                                    conI.Port = Connection.Protocol.VNC.Defaults.Port
                                Else
                                    conI.Port = Connection.Protocol.RDP.Defaults.Port
                                End If
                            End If
                            conI.UseConsoleSession = False
                        End If

                        If Me.confVersion > 0.4 Then '0.5 and 0.6
                            conI.RedirectDiskDrives = .Attributes("RedirectDiskDrives").Value
                            conI.RedirectPrinters = .Attributes("RedirectPrinters").Value
                            conI.RedirectPorts = .Attributes("RedirectPorts").Value
                            conI.RedirectSmartCards = .Attributes("RedirectSmartCards").Value
                        Else
                            conI.RedirectDiskDrives = False
                            conI.RedirectPrinters = False
                            conI.RedirectPorts = False
                            conI.RedirectSmartCards = False
                        End If

                        If Me.confVersion > 0.6 Then '0.7
                            conI.Protocol = Tools.Misc.StringToEnum(GetType(Connection.Protocol.Protocols), .Attributes("Protocol").Value)
                            conI.Port = .Attributes("Port").Value
                        End If

                        If Me.confVersion > 0.9 Then '1.0
                            conI.RedirectKeys = .Attributes("RedirectKeys").Value
                        End If

                        If Me.confVersion > 1.1 Then '1.2
                            conI.PuttySession = .Attributes("PuttySession").Value
                        End If

                        If Me.confVersion > 1.2 Then '1.3
                            conI.Colors = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.RDPColors), .Attributes("Colors").Value)
                            conI.Resolution = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.RDPResolutions), .Attributes("Resolution").Value)
                            conI.RedirectSound = Tools.Misc.StringToEnum(GetType(Connection.Protocol.RDP.RDPSounds), .Attributes("RedirectSound").Value)
                        Else
                            Select Case .Attributes("Colors").Value
                                Case 0
                                    conI.Colors = Connection.Protocol.RDP.RDPColors.Colors256
                                Case 1
                                    conI.Colors = Connection.Protocol.RDP.RDPColors.Colors16Bit
                                Case 2
                                    conI.Colors = Connection.Protocol.RDP.RDPColors.Colors24Bit
                                Case 3
                                    conI.Colors = Connection.Protocol.RDP.RDPColors.Colors32Bit
                                Case 4
                                    conI.Colors = Connection.Protocol.RDP.RDPColors.Colors15Bit
                            End Select

                            conI.RedirectSound = .Attributes("RedirectSound").Value
                        End If

                        If Me.confVersion > 1.2 Then '1.3
                            conI.Inherit = New Connection.Info.Inheritance(conI)
                            conI.Inherit.CacheBitmaps = .Attributes("InheritCacheBitmaps").Value
                            conI.Inherit.Colors = .Attributes("InheritColors").Value
                            conI.Inherit.Description = .Attributes("InheritDescription").Value
                            conI.Inherit.DisplayThemes = .Attributes("InheritDisplayThemes").Value
                            conI.Inherit.DisplayWallpaper = .Attributes("InheritDisplayWallpaper").Value
                            conI.Inherit.Domain = .Attributes("InheritDomain").Value
                            conI.Inherit.Icon = .Attributes("InheritIcon").Value
                            conI.Inherit.Panel = .Attributes("InheritPanel").Value
                            conI.Inherit.Password = .Attributes("InheritPassword").Value
                            conI.Inherit.Port = .Attributes("InheritPort").Value
                            conI.Inherit.Protocol = .Attributes("InheritProtocol").Value
                            conI.Inherit.PuttySession = .Attributes("InheritPuttySession").Value
                            conI.Inherit.RedirectDiskDrives = .Attributes("InheritRedirectDiskDrives").Value
                            conI.Inherit.RedirectKeys = .Attributes("InheritRedirectKeys").Value
                            conI.Inherit.RedirectPorts = .Attributes("InheritRedirectPorts").Value
                            conI.Inherit.RedirectPrinters = .Attributes("InheritRedirectPrinters").Value
                            conI.Inherit.RedirectSmartCards = .Attributes("InheritRedirectSmartCards").Value
                            conI.Inherit.RedirectSound = .Attributes("InheritRedirectSound").Value
                            conI.Inherit.Resolution = .Attributes("InheritResolution").Value
                            conI.Inherit.UseConsoleSession = .Attributes("InheritUseConsoleSession").Value
                            conI.Inherit.Username = .Attributes("InheritUsername").Value

                            conI.Icon = .Attributes("Icon").Value
                            conI.Panel = .Attributes("Panel").Value
                        Else
                            conI.Inherit = New Connection.Info.Inheritance(conI, .Attributes("Inherit").Value)

                            conI.Icon = .Attributes("Icon").Value.Replace(".ico", "")
                            conI.Panel = My.Language.strGeneral
                        End If

                        If Me.confVersion > 1.4 Then '1.5
                            conI.PleaseConnect = .Attributes("Connected").Value
                        End If

                        If Me.confVersion > 1.5 Then '1.6
                            conI.PreExtApp = .Attributes("PreExtApp").Value
                            conI.PostExtApp = .Attributes("PostExtApp").Value
                            conI.Inherit.PreExtApp = .Attributes("InheritPreExtApp").Value
                            conI.Inherit.PostExtApp = .Attributes("InheritPostExtApp").Value
                        End If

                        If Me.confVersion > 1.6 Then '1.7
                            conI.VNCCompression = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.VNC.Compression), .Attributes("VNCCompression").Value)
                            conI.VNCEncoding = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.VNC.Encoding), .Attributes("VNCEncoding").Value)
                            conI.VNCAuthMode = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.VNC.AuthMode), .Attributes("VNCAuthMode").Value)
                            conI.VNCProxyType = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.VNC.ProxyType), .Attributes("VNCProxyType").Value)
                            conI.VNCProxyIP = .Attributes("VNCProxyIP").Value
                            conI.VNCProxyPort = .Attributes("VNCProxyPort").Value
                            conI.VNCProxyUsername = .Attributes("VNCProxyUsername").Value
                            conI.VNCProxyPassword = Security.Crypt.Decrypt(.Attributes("VNCProxyPassword").Value, pW)
                            conI.VNCColors = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.VNC.Colors), .Attributes("VNCColors").Value)
                            conI.VNCSmartSizeMode = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.VNC.SmartSizeMode), .Attributes("VNCSmartSizeMode").Value)
                            conI.VNCViewOnly = .Attributes("VNCViewOnly").Value

                            conI.Inherit.VNCCompression = .Attributes("InheritVNCCompression").Value
                            conI.Inherit.VNCEncoding = .Attributes("InheritVNCEncoding").Value
                            conI.Inherit.VNCAuthMode = .Attributes("InheritVNCAuthMode").Value
                            conI.Inherit.VNCProxyType = .Attributes("InheritVNCProxyType").Value
                            conI.Inherit.VNCProxyIP = .Attributes("InheritVNCProxyIP").Value
                            conI.Inherit.VNCProxyPort = .Attributes("InheritVNCProxyPort").Value
                            conI.Inherit.VNCProxyUsername = .Attributes("InheritVNCProxyUsername").Value
                            conI.Inherit.VNCProxyPassword = .Attributes("InheritVNCProxyPassword").Value
                            conI.Inherit.VNCColors = .Attributes("InheritVNCColors").Value
                            conI.Inherit.VNCSmartSizeMode = .Attributes("InheritVNCSmartSizeMode").Value
                            conI.Inherit.VNCViewOnly = .Attributes("InheritVNCViewOnly").Value
                        End If

                        If Me.confVersion > 1.7 Then '1.8
                            conI.RDPAuthenticationLevel = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.RDP.AuthenticationLevel), .Attributes("RDPAuthenticationLevel").Value)

                            conI.Inherit.RDPAuthenticationLevel = .Attributes("InheritRDPAuthenticationLevel").Value
                        End If

                        If Me.confVersion > 1.8 Then '1.9
                            conI.RenderingEngine = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.HTTPBase.RenderingEngine), .Attributes("RenderingEngine").Value)
                            conI.MacAddress = .Attributes("MacAddress").Value

                            conI.Inherit.RenderingEngine = .Attributes("InheritRenderingEngine").Value
                            conI.Inherit.MacAddress = .Attributes("InheritMacAddress").Value
                        End If

                        If Me.confVersion > 1.9 Then '2.0
                            conI.UserField = .Attributes("UserField").Value
                            conI.Inherit.UserField = .Attributes("InheritUserField").Value
                        End If

                        If Me.confVersion > 2.0 Then '2.1
                            conI.ExtApp = .Attributes("ExtApp").Value
                            conI.Inherit.ExtApp = .Attributes("InheritExtApp").Value
                        End If

                        If Me.confVersion > 2.1 Then '2.2
                            ' Get settings
                            conI.RDGatewayUsageMethod = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.RDP.RDGatewayUsageMethod), .Attributes("RDGatewayUsageMethod").Value)
                            conI.RDGatewayHostname = .Attributes("RDGatewayHostname").Value
                            conI.RDGatewayUseConnectionCredentials = Tools.Misc.StringToEnum(GetType(mRemoteNG.Connection.Protocol.RDP.RDGatewayUseConnectionCredentials), .Attributes("RDGatewayUseConnectionCredentials").Value)
                            conI.RDGatewayUsername = .Attributes("RDGatewayUsername").Value
                            conI.RDGatewayPassword = Security.Crypt.Decrypt(.Attributes("RDGatewayPassword").Value, pW)
                            conI.RDGatewayDomain = .Attributes("RDGatewayDomain").Value

                            ' Get inheritance settings
                            conI.Inherit.RDGatewayUsageMethod = .Attributes("InheritRDGatewayUsageMethod").Value
                            conI.Inherit.RDGatewayHostname = .Attributes("InheritRDGatewayHostname").Value
                            conI.Inherit.RDGatewayUseConnectionCredentials = .Attributes("InheritRDGatewayUseConnectionCredentials").Value
                            conI.Inherit.RDGatewayUsername = .Attributes("InheritRDGatewayUsername").Value
                            conI.Inherit.RDGatewayPassword = .Attributes("InheritRDGatewayPassword").Value
                            conI.Inherit.RDGatewayDomain = .Attributes("InheritRDGatewayDomain").Value
                        End If

                        If Me.confVersion > 2.2 Then '2.3
                            ' Get settings
                            conI.EnableFontSmoothing = .Attributes("EnableFontSmoothing").Value
                            conI.EnableDesktopComposition = .Attributes("EnableDesktopComposition").Value

                            ' Get inheritance settings
                            conI.Inherit.EnableFontSmoothing = .Attributes("InheritEnableFontSmoothing").Value
                            conI.Inherit.EnableDesktopComposition = .Attributes("InheritEnableDesktopComposition").Value
                        End If

                        If confVersion >= 2.4 Then
                            conI.UseCredSsp = .Attributes("UseCredSsp").Value
                            conI.Inherit.UseCredSsp = .Attributes("InheritUseCredSsp").Value
                        End If

                        If confVersion >= 2.5 Then
                            conI.LoadBalanceInfo = .Attributes("LoadBalanceInfo").Value
                            conI.AutomaticResize = .Attributes("AutomaticResize").Value
                            conI.Inherit.LoadBalanceInfo = .Attributes("InheritLoadBalanceInfo").Value
                            conI.Inherit.AutomaticResize = .Attributes("InheritAutomaticResize").Value
                        End If
                    End With
                Catch ex As Exception
                    MessageCollector.AddMessage(Messages.MessageClass.ErrorMsg, String.Format(My.Language.strGetConnectionInfoFromXmlFailed, conI.Name, Me.ConnectionFileName, ex.Message), False)
                End Try
                Return conI
            End Function

            Private Function Authenticate(ByVal Value As String, ByVal CompareToOriginalValue As Boolean, Optional ByVal rootInfo As Root.Info = Nothing) As Boolean
                Dim passwordName As String
                If UseSQL Then
                    passwordName = Language.strSQLServer.TrimEnd(":")
                Else
                    passwordName = Path.GetFileName(ConnectionFileName)
                End If

                If CompareToOriginalValue Then
                    Do Until Security.Crypt.Decrypt(Value, pW) <> Value
                        pW = Tools.Misc.PasswordDialog(passwordName, False)

                        If pW = "" Then
                            Return False
                        End If
                    Loop
                Else
                    Do Until Security.Crypt.Decrypt(Value, pW) = "ThisIsProtected"
                        pW = Tools.Misc.PasswordDialog(passwordName, False)

                        If pW = "" Then
                            Return False
                        End If
                    Loop

                    If rootInfo IsNot Nothing Then
                        rootInfo.Password = True
                        rootInfo.PasswordString = pW
                    End If
                End If

                Return True
            End Function
#End Region
        End Class
    End Namespace
End Namespace