Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Globalization
Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports System.Reflection
Imports System.Runtime.InteropServices
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Documents
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Imaging
Imports System.Windows.Threading
Imports Microsoft.Win32
Imports TscLabel.Core

Namespace TscLabel.App
    Public Partial Class MainWindow
        Inherits Window

        Private ReadOnly _profiles As New ObservableCollection(Of LabelProfile)()
        Private ReadOnly _printers As New ObservableCollection(Of PrinterProfile)()
        Private ReadOnly _supvanDevices As New ObservableCollection(Of SupvanUsbDevice)()
        Private ReadOnly _historyEntries As New ObservableCollection(Of PrintHistoryEntry)()
        Private ReadOnly _visibleTemplates As New ObservableCollection(Of LabelTemplateDefinition)()
        Private ReadOnly _fitTimer As New DispatcherTimer With {.Interval = TimeSpan.FromMilliseconds(280)}
        Private _allTemplates As New List(Of LabelTemplateDefinition)()
        Private _localTemplates As New List(Of LabelTemplateDefinition)()
        Private _sharedTemplates As New List(Of LabelTemplateDefinition)()
        Private _settings As AppSettings
        Private _newestLaborPath As String = ""
        Private _isInitializing As Boolean = True
        Private _isSynchronizingFormatControls As Boolean
        Private _isSynchronizingProfileSelection As Boolean
        Private _isLoadingPrinterEditor As Boolean
        Private _editingNewPrinter As Boolean
        Private _contentFits As Boolean = True
        Private _isPrinting As Boolean
        Private _selectedImage As Image
        Private _backgroundImageSelected As Boolean
        Private _selectedImageAdorner As SelectedImageAdorner
        Private _selectedImageAdornerLayer As AdornerLayer
        Private _selectedTemplate As LabelTemplateDefinition
        Private _activeTemplate As LabelTemplateDefinition
        Private _templateClipboardXaml As String = ""
        Private _templateLoadError As String = ""
        Private _isSynchronizingTemplateSelection As Boolean
        Private _isTemplateDesignMode As Boolean
        Private _isApplyingTemplate As Boolean
        Private ReadOnly _processedBrainOfficeClipboardKeys As New HashSet(Of String)(StringComparer.Ordinal)
        Private _isImportingBrainOfficeClipboard As Boolean
        Private _lastBrainOfficeTransfer As BrainOfficeClipboardTransfer
        Private _autoConfiguredPrinterCount As Integer
        Private ReadOnly _brainOfficeLaunchResults As New Dictionary(Of String, BrainOfficeTemplateLaunchResult)(StringComparer.Ordinal)

        <DllImport("user32.dll")>
        Private Shared Function SetForegroundWindow(windowHandle As IntPtr) As Boolean
        End Function

        Public Sub New()
            InitializeComponent()
            FormattingMarks.Editor = LabelEditor
            AddHandler LabelEditor.LayoutUpdated, Sub()
                If FormattingMarks.IsVisible Then FormattingMarks.InvalidateVisual()
            End Sub
            ApplyBuildIdentity()
            _settings = SettingsStore.Load()
            AddHandler Loaded, AddressOf WindowLoaded
            AddHandler Activated, AddressOf WindowActivated
            AddHandler Closing, AddressOf WindowClosing
            AddHandler SizeChanged, AddressOf WindowSizeChanged
            AddHandler _fitTimer.Tick, AddressOf FitTimerTick
            InitializeProfiles()
            InitializePrinters()
            SupvanDeviceCombo.ItemsSource = _supvanDevices
            InitializeFontsAndSizes()
            HistoryList.ItemsSource = _historyEntries
            TemplateList.ItemsSource = _visibleTemplates
            CompactTemplateList.ItemsSource = _visibleTemplates
            TemplateDesignList.ItemsSource = _visibleTemplates
            TemplatePrinterSelectionList.ItemsSource = _printers
            PlaceholderItemsControl.ItemsSource = LabelTemplateEngine.Placeholders
            HistoryPathText.Text = "Portable Datei: " & PrintHistoryStore.DefaultPath
            InitializeTemplates()
            ApplySettingsToControls()
            SetLabelDocument(LabelDocumentFactory.CreateFreeTemplate())
            RestoreLastUsedTemplate()
            HistoryPreviewEditor.Document = LabelDocumentFactory.CreateBaseDocument()
            _isInitializing = False
        End Sub

        Private Sub ApplyBuildIdentity()
            Dim identity = GetBuildIdentity()
            HeaderBuildText.Text = identity
            Title = "Etikettenstudio · " & identity
        End Sub

        Private Shared Function GetBuildIdentity() As String
            Dim assembly = GetType(MainWindow).Assembly
            Dim informational = assembly.GetCustomAttribute(Of AssemblyInformationalVersionAttribute)()?.InformationalVersion
            If String.IsNullOrWhiteSpace(informational) Then Return "Build unbekannt"
            Dim parts = informational.Split("+"c, 2)
            Dim versionText = parts(0)
            If parts.Length < 2 OrElse String.Equals(parts(1), "DEV", StringComparison.OrdinalIgnoreCase) Then Return $"v{versionText} · Entwicklungsbuild"
            Dim builtAt As DateTime
            If DateTime.TryParseExact(parts(1), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, builtAt) Then
                Return $"v{versionText} · Build {builtAt:dd.MM.yyyy HH:mm:ss}"
            End If
            Return $"v{versionText} · Build {parts(1)}"
        End Function

        Private Sub WindowLoaded(sender As Object, e As RoutedEventArgs)
            Try
                _autoConfiguredPrinterCount = SettingsStore.ApplyDetectedWindowsPrinterPresets(_settings, WindowsPrinterService.GetInstalledPrinters())
                If _autoConfiguredPrinterCount > 0 Then
                    SettingsStore.Save(_settings)
                    For Each printer In _settings.Printers
                        If Not _printers.Any(Function(item) String.Equals(item.Id, printer.Id, StringComparison.OrdinalIgnoreCase)) Then _printers.Add(printer.Copy())
                    Next
                End If
            Catch
                ' Ein defekter Spooler darf den Programmstart nicht verhindern.
            End Try
            UpdateResponsiveTemplateLayout()
            UpdateLabelDimensions()
            RefreshNewestLaborFile()
            Dim hasExplicitInput = LoadCommandLineInput()
            RefreshHistory()
            ScheduleFitCheck()
            Dim explicitClipboard = Environment.GetCommandLineArgs().Any(Function(arg) String.Equals(arg, "--brainoffice-clipboard", StringComparison.OrdinalIgnoreCase))
            Dim imported = If(explicitClipboard OrElse Not hasExplicitInput, TryImportBrainOfficeClipboard(explicitClipboard), False)
            If Not imported AndAlso _autoConfiguredPrinterCount > 0 Then
                SetStatus("Zebra ZD410 automatisch als ZPL-Drucker mit 50 × 25 mm eingerichtet.", StatusKind.Success)
            ElseIf Not imported Then
                SetStatus("Bereit. Text eingeben, Clipboard-Screencut oder Laborauftrag übernehmen.", StatusKind.Neutral)
            End If
            If Not String.IsNullOrWhiteSpace(_templateLoadError) Then
                ShowInfo("Vorlagen konnten nicht geladen werden", _templateLoadError, LabelTemplateStore.DefaultPath, True)
            End If
        End Sub

        Private Sub WindowActivated(sender As Object, e As EventArgs)
            If _isInitializing OrElse Not IsLoaded Then Return
            If _isTemplateDesignMode Then
                SetStatus("Vorlagenmodus aktiv · BrainOffice-Daten verändern den geschützten Entwurf nicht.", StatusKind.Neutral)
                Return
            End If
            TryImportBrainOfficeClipboard(False)
        End Sub

        Private Sub InitializeTemplates()
            ReloadTemplateStores()
        End Sub

        Private Sub ReloadTemplateStores()
            _templateLoadError = ""
            Try
                _localTemplates = LabelTemplateStore.Load()
                For Each entry In _localTemplates
                    entry.StoreScope = "LOCAL"
                Next
            Catch ex As Exception
                _localTemplates = New List(Of LabelTemplateDefinition)()
                _templateLoadError = ex.Message
            End Try

            _sharedTemplates = New List(Of LabelTemplateDefinition)()
            Dim sharedPath = GetSharedTemplatePath()
            If Not String.IsNullOrWhiteSpace(sharedPath) Then
                Try
                    If File.Exists(sharedPath) Then
                        _sharedTemplates = LabelTemplateStore.Load(sharedPath)
                    Else
                        _sharedTemplates = New List(Of LabelTemplateDefinition)()
                    End If
                    For Each entry In _sharedTemplates
                        entry.StoreScope = "SHARED"
                    Next
                Catch ex As Exception
                    If String.IsNullOrWhiteSpace(_templateLoadError) Then
                        _templateLoadError = "Zentrale Vorlagen konnten nicht geladen werden: " & ex.Message
                    Else
                        _templateLoadError &= Environment.NewLine & "Zentrale Vorlagen konnten nicht geladen werden: " & ex.Message
                    End If
                End Try
            End If

            _allTemplates = _localTemplates.Concat(_sharedTemplates).
                OrderBy(Function(entry) entry.Name, StringComparer.CurrentCultureIgnoreCase).
                ThenBy(Function(entry) entry.StoreScope, StringComparer.OrdinalIgnoreCase).
                ToList()
            UpdateSharedTemplateStatus()
        End Sub

        Private Function GetSharedTemplatePath() As String
            Return If(_settings?.SharedTemplatePath, "").Trim()
        End Function

        Private Function HasSharedTemplateStore() As Boolean
            Return Not String.IsNullOrWhiteSpace(GetSharedTemplatePath())
        End Function

        Private Shared Function NormalizeTemplateSaveTarget(value As String) As String
            Return If(String.Equals(If(value, "").Trim(), "SHARED", StringComparison.OrdinalIgnoreCase), "SHARED", "LOCAL")
        End Function

        Private Sub UpdateSharedTemplateStatus()
            If SharedTemplateStatusText Is Nothing Then Return
            Dim sharedPath = GetSharedTemplatePath()
            If String.IsNullOrWhiteSpace(sharedPath) Then
                SharedTemplateStatusText.Text = "Zentraler Speicher nicht eingerichtet. Vorlagen werden lokal gespeichert."
            ElseIf File.Exists(sharedPath) Then
                SharedTemplateStatusText.Text = $"Zentral: {_sharedTemplates.Count} Vorlage(n) · {sharedPath}"
            Else
                SharedTemplateStatusText.Text = "Zentrale Datei wird beim ersten zentralen Speichern angelegt: " & sharedPath
            End If
        End Sub

        Private Sub PersistTemplatesForScope(scope As String)
            Dim normalized = NormalizeTemplateSaveTarget(scope)
            If normalized = "SHARED" Then
                Dim sharedPath = GetSharedTemplatePath()
                If String.IsNullOrWhiteSpace(sharedPath) Then Throw New InvalidOperationException("Bitte zuerst einen zentralen Vorlagenpfad eintragen.")
                LabelTemplateStore.Save(_sharedTemplates, sharedPath)
            Else
                LabelTemplateStore.Save(_localTemplates)
            End If
            _allTemplates = _localTemplates.Concat(_sharedTemplates).
                OrderBy(Function(entry) entry.Name, StringComparer.CurrentCultureIgnoreCase).
                ThenBy(Function(entry) entry.StoreScope, StringComparer.OrdinalIgnoreCase).
                ToList()
            UpdateSharedTemplateStatus()
        End Sub

        Private Sub RestoreLastUsedTemplate()
            If String.IsNullOrWhiteSpace(_settings.LastUsedTemplateId) Then Return
            Dim entry = _allTemplates.FirstOrDefault(Function(item) String.Equals(item.Id, _settings.LastUsedTemplateId, StringComparison.OrdinalIgnoreCase))
            If entry Is Nothing Then
                _settings.LastUsedTemplateId = ""
                Return
            End If
            Try
                SetLabelDocument(LabelTemplateStore.RestoreDocument(entry))
                _activeTemplate = entry
                SetSelectedTemplate(entry)
            Catch
                _settings.LastUsedTemplateId = ""
            End Try
        End Sub

        Private Sub WindowClosing(sender As Object, e As CancelEventArgs)
            Try
                _settings.WindowWidth = ActualWidth
                _settings.WindowHeight = ActualHeight
                ReadPrinterSettingsFromControls()
                SettingsStore.Save(_settings)
            Catch
                ' Schließen darf nie durch eine lokale Konfiguration blockiert werden.
            End Try
        End Sub

        Private Sub InitializeProfiles()
            For Each profile In LabelProfile.StandardProfiles()
                _profiles.Add(profile.Copy())
            Next
            For Each profile In _settings.CustomProfiles
                Dim copy = profile.Copy()
                copy.IsCustom = True
                _profiles.Add(copy)
            Next
            ProfileCombo.ItemsSource = _profiles
            SettingsProfileList.ItemsSource = _profiles
            PrinterProfileCombo.ItemsSource = _profiles
        End Sub

        Private Sub InitializePrinters()
            For Each printer In _settings.Printers
                _printers.Add(printer.Copy())
            Next
            TargetPrinterCombo.ItemsSource = _printers
            SettingsPrinterList.ItemsSource = _printers
        End Sub

        Private Sub InitializeFontsAndSizes()
            FontFamilyCombo.ItemsSource = Fonts.SystemFontFamilies.Select(Function(font) font.Source).OrderBy(Function(name) name).ToList()
            FontFamilyCombo.SelectedItem = "Segoe UI"
            FontSizeCombo.ItemsSource = New Double() {4, 4.5, 5, 5.5, 6, 6.5, 7, 7.5, 8, 8.5, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28}
            FontSizeCombo.SelectedItem = 11.0
            LineHeightCombo.ItemsSource = New Object() {"Auto", 4, 5, 6, 7, 8, 9, 10, 12, 14, 16, 18, 20}
            LineHeightCombo.SelectedItem = "Auto"
            QuickMarginCombo.ItemsSource = New Double() {0, 0.5, 1, 1.5, 2, 2.5, 3}
            ImageModeCombo.SelectedIndex = 0
            ImageAlignmentCombo.SelectedIndex = 1
            ImageOpacityCombo.SelectedIndex = 0
        End Sub

        Private Sub ApplySettingsToControls()
            InversePrintCheck.IsChecked = Not _settings.NormalPrint
            SaveHistoryCheck.IsChecked = _settings.HistoryEnabled
            DownloadDirectoryBox.Text = _settings.DownloadDirectory
            PracticeLineBox.Text = _settings.PracticeLine
            SharedTemplatePathBox.Text = _settings.SharedTemplatePath
            SelectComboByTag(TemplateSaveTargetCombo, _settings.TemplateSaveTarget)
            SelectComboByTag(TemplateSaveTargetInlineCombo, _settings.TemplateSaveTarget)
            SettingsPathText.Text = "Lokale Konfiguration: " & SettingsStore.DefaultPath
            Width = Math.Max(MinWidth, _settings.WindowWidth)
            Height = Math.Max(MinHeight, _settings.WindowHeight)

            Dim selected = _profiles.FirstOrDefault(Function(profile) profile.Id = _settings.SelectedProfileId)
            If selected Is Nothing Then selected = _profiles.First()
            ProfileCombo.SelectedItem = selected
            SettingsProfileList.SelectedItem = selected

            Dim selectedPrinter = _printers.FirstOrDefault(Function(printer) String.Equals(printer.Id, _settings.SelectedPrinterId, StringComparison.OrdinalIgnoreCase))
            If selectedPrinter Is Nothing Then selectedPrinter = _printers.First()
            TargetPrinterCombo.SelectedItem = selectedPrinter
            SettingsPrinterList.SelectedItem = selectedPrinter
            LoadPrinterIntoEditor(selectedPrinter)
            UpdateQuickMarginControl(selectedPrinter)
            UpdatePrinterSummary()
            RefreshTemplatesForPrinter()
            UpdateSharedTemplateStatus()
        End Sub

        Private Function CurrentBaseProfile() As LabelProfile
            Return TryCast(ProfileCombo.SelectedItem, LabelProfile)
        End Function

        Private Function CurrentProfile() As LabelProfile
            Dim profile = CurrentBaseProfile()
            If profile Is Nothing Then Return Nothing
            Dim printer = CurrentPrinter()
            Return profile.CreateOriented(printer IsNot Nothing AndAlso printer.SwapLabelOrientation)
        End Function

        Private Function CurrentPrinter() As PrinterProfile
            Return TryCast(TargetPrinterCombo.SelectedItem, PrinterProfile)
        End Function

        Private Sub ProfileSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing Then Return
            Dim profile = CurrentBaseProfile()
            If profile Is Nothing Then Return
            _settings.SelectedProfileId = profile.Id
            If Not _isApplyingTemplate Then SettingsStore.Save(_settings)
            SetSettingsProfileSelection(profile)
            UpdateLabelDimensions()
            ScheduleFitCheck()
            SetStatus($"Format gewählt: {profile.WidthMm:0.##} × {profile.HeightMm:0.##} mm.", StatusKind.Neutral)
        End Sub

        Private Sub TargetPrinterSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing Then Return
            Dim printer = CurrentPrinter()
            If printer Is Nothing Then Return
            _settings.SelectedPrinterId = printer.Id
            SettingsPrinterList.SelectedItem = printer
            ApplyPrinterPreferredProfile(printer)
            LoadPrinterIntoEditor(printer)
            SyncPrintersToSettings()
            SyncLegacyPrinterFields(printer)
            SettingsStore.Save(_settings)
            ConnectionDot.Fill = New SolidColorBrush(Color.FromRgb(184, 214, 214))
            UpdateQuickMarginControl(printer)
            UpdateLabelDimensions()
            ScheduleFitCheck()
            UpdatePrinterSummary()
            RefreshTemplatesForPrinter(If(_isApplyingTemplate, _selectedTemplate?.Id, ""))
            SetStatus($"Zieldrucker gewählt: {printer.Name} · {printer.ConnectionText}.", StatusKind.Neutral)
        End Sub

        Private Sub ApplyPrinterPreferredProfile(printer As PrinterProfile)
            If printer Is Nothing OrElse String.IsNullOrWhiteSpace(printer.PreferredProfileId) Then Return
            Dim preferred = _profiles.FirstOrDefault(Function(profile) String.Equals(profile.Id, printer.PreferredProfileId, StringComparison.OrdinalIgnoreCase))
            If preferred Is Nothing Then Return
            ProfileCombo.SelectedItem = preferred
            SetSettingsProfileSelection(preferred)
            _settings.SelectedProfileId = preferred.Id
        End Sub

        Private Sub SetSettingsProfileSelection(profile As LabelProfile)
            If profile Is Nothing OrElse SettingsProfileList Is Nothing Then Return
            _isSynchronizingProfileSelection = True
            Try
                SettingsProfileList.SelectedItem = profile
            Finally
                _isSynchronizingProfileSelection = False
            End Try
        End Sub

        Private Sub SettingsPrinterSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse _isLoadingPrinterEditor Then Return
            Dim printer = TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)
            If printer Is Nothing Then Return
            _editingNewPrinter = False
            LoadPrinterIntoEditor(printer)
        End Sub

        Private Sub LoadPrinterIntoEditor(printer As PrinterProfile)
            If printer Is Nothing Then Return
            _isLoadingPrinterEditor = True
            Try
                PrinterNameBox.Text = printer.Name
                SelectComboByTag(PrinterConnectionModeCombo, printer.ConnectionMode)
                PrinterIpBox.Text = printer.Host
                PrinterPortBox.Text = printer.Port.ToString(CultureInfo.InvariantCulture)
                PrinterQueueBox.Text = printer.QueueName
                SupvanAliasBox.Text = printer.SupvanAlias
                SelectComboByTag(PrinterLanguageCombo, printer.Language)
                SelectComboByTag(PrinterDpiCombo, printer.Dpi.ToString(CultureInfo.InvariantCulture))
                PrinterProfileCombo.SelectedItem = _profiles.FirstOrDefault(Function(profile) String.Equals(profile.Id, printer.PreferredProfileId, StringComparison.OrdinalIgnoreCase))
                If PrinterProfileCombo.SelectedItem Is Nothing Then PrinterProfileCombo.SelectedItem = CurrentBaseProfile()
                SetSettingsProfileSelection(TryCast(PrinterProfileCombo.SelectedItem, LabelProfile))
                SwapLabelOrientationCheck.IsChecked = printer.SwapLabelOrientation
                SelectComboByTag(RasterRotationCombo, printer.RasterRotationDegrees.ToString(CultureInfo.InvariantCulture))
                DensitySlider.Value = printer.Density
                SpeedSlider.Value = printer.Speed
                MarginBox.Text = printer.MarginMm.ToString("0.##", CultureInfo.CurrentCulture)
                CutterCheck.IsChecked = printer.CutterEnabled
                RefreshSupvanDevices(printer)
                TargetPrinterSettingsText.Text = "Aktiver Zieldrucker: " & If(CurrentPrinter()?.Name, printer.Name)
                PrinterManagerStatusText.Text = $"Bearbeitung: {printer.Name} · {printer.ConnectionText}"
                UpdatePrinterEditorVisibility()
            Finally
                _isLoadingPrinterEditor = False
            End Try
        End Sub

        Private Shared Sub SelectComboByTag(combo As ComboBox, tagValue As String)
            For Each item In combo.Items.OfType(Of ComboBoxItem)()
                If String.Equals(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), tagValue, StringComparison.OrdinalIgnoreCase) Then
                    combo.SelectedItem = item
                    Return
                End If
            Next
            combo.SelectedIndex = 0
        End Sub

        Private Shared Function ComboTag(combo As ComboBox) As String
            Dim item = TryCast(combo?.SelectedItem, ComboBoxItem)
            Return Convert.ToString(item?.Tag, CultureInfo.InvariantCulture)
        End Function

        Private Function LooksLikeSupvanProfile() As Boolean
            Return PrinterNameBox.Text.Contains("SUPVAN", StringComparison.OrdinalIgnoreCase) OrElse
                   SupvanDeviceCombo.SelectedItem IsNot Nothing OrElse
                   Not String.IsNullOrWhiteSpace(SupvanAliasBox.Text)
        End Function

        Private Sub SelectDirectSupvanMode(Optional refreshDevices As Boolean = True)
            SelectComboByTag(PrinterConnectionModeCombo, PrinterProfile.SupvanUsbConnection)
            SelectComboByTag(PrinterLanguageCombo, PrinterProfile.SupvanRasterLanguage)
            SelectComboByTag(PrinterDpiCombo, "203")
            Dim supvanProfile = _profiles.FirstOrDefault(Function(profile) profile.Id = "std-supvan-50x30")
            If supvanProfile Is Nothing Then supvanProfile = _profiles.FirstOrDefault(Function(profile) profile.Id = "std-supvan-40x30")
            If supvanProfile IsNot Nothing Then PrinterProfileCombo.SelectedItem = supvanProfile
            SelectComboByTag(RasterRotationCombo, "0")
            CutterCheck.IsChecked = False
            If refreshDevices Then RefreshSupvanDevices(TryCast(SettingsPrinterList.SelectedItem, PrinterProfile))
            UpdatePrinterEditorVisibility()
        End Sub

        Private Sub NewPrinterClick(sender As Object, e As RoutedEventArgs)
            _editingNewPrinter = True
            _isLoadingPrinterEditor = True
            Try
                SettingsPrinterList.SelectedItem = Nothing
                PrinterNameBox.Text = ""
                SelectComboByTag(PrinterConnectionModeCombo, PrinterProfile.TcpRawConnection)
                PrinterIpBox.Text = ""
                PrinterPortBox.Text = "9100"
                PrinterQueueBox.Text = ""
                SupvanAliasBox.Text = ""
                SelectComboByTag(PrinterLanguageCombo, PrinterProfile.TsplLanguage)
                SelectComboByTag(PrinterDpiCombo, "203")
                PrinterProfileCombo.SelectedItem = CurrentBaseProfile()
                SwapLabelOrientationCheck.IsChecked = False
                SelectComboByTag(RasterRotationCombo, "0")
                DensitySlider.Value = 8
                SpeedSlider.Value = 4
                MarginBox.Text = "1,5"
                CutterCheck.IsChecked = False
                RefreshSupvanDevices(Nothing)
                UpdatePrinterEditorVisibility()
                PrinterManagerStatusText.Text = "Neuer Drucker: Druckweg wählen, Adresse oder Windows-Warteschlange eintragen und übernehmen."
            Finally
                _isLoadingPrinterEditor = False
            End Try
            PrinterNameBox.Focus()
            SetStatus("Neuen Drucker erfassen – noch nicht gespeichert.", StatusKind.Neutral)
        End Sub

        Private Sub UpdatePrinterClick(sender As Object, e As RoutedEventArgs)
            Try
                Dim selected = TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)
                Dim edited = ReadPrinterEditor(If(selected?.Id, ""))
                If _editingNewPrinter OrElse selected Is Nothing Then
                    edited.Id = "printer-" & Guid.NewGuid().ToString("N")
                    _printers.Add(edited)
                    SettingsPrinterList.SelectedItem = edited
                    _editingNewPrinter = False
                    PrinterManagerStatusText.Text = $"Drucker hinzugefügt: {edited.Name} · {edited.ConnectionText}"
                    SetStatus($"Drucker {edited.Name} hinzugefügt.", StatusKind.Success)
                Else
                    selected.Name = edited.Name
                    selected.ConnectionMode = edited.ConnectionMode
                    selected.Host = edited.Host
                    selected.Port = edited.Port
                    selected.QueueName = edited.QueueName
                    selected.SupvanDeviceId = edited.SupvanDeviceId
                    selected.SupvanDevicePath = edited.SupvanDevicePath
                    selected.SupvanSerialNumber = edited.SupvanSerialNumber
                    selected.SupvanAlias = edited.SupvanAlias
                    selected.SupvanVid = edited.SupvanVid
                    selected.SupvanPid = edited.SupvanPid
                    selected.SupvanManufacturer = edited.SupvanManufacturer
                    selected.SupvanProductName = edited.SupvanProductName
                    selected.Language = edited.Language
                    selected.Dpi = edited.Dpi
                    selected.Density = edited.Density
                    selected.Speed = edited.Speed
                    selected.MarginMm = edited.MarginMm
                    selected.PreferredProfileId = edited.PreferredProfileId
                    selected.SwapLabelOrientation = edited.SwapLabelOrientation
                    selected.RasterRotationDegrees = edited.RasterRotationDegrees
                    selected.CutterEnabled = edited.CutterEnabled
                    SettingsPrinterList.Items.Refresh()
                    TargetPrinterCombo.Items.Refresh()
                    PrinterManagerStatusText.Text = $"Änderungen gespeichert: {selected.Name} · {selected.ConnectionText}"
                    If CurrentPrinter() Is selected Then
                        SyncLegacyPrinterFields(selected)
                        UpdateQuickMarginControl(selected)
                        ApplyPrinterPreferredProfile(selected)
                        UpdateLabelDimensions()
                    End If
                    SetStatus($"Drucker {selected.Name} aktualisiert.", StatusKind.Success)
                End If
                SyncPrintersToSettings()
                SettingsStore.Save(_settings)
                UpdatePrinterSummary()
            Catch ex As Exception
                ShowInfo("Drucker nicht gespeichert", ex.Message, "Name sowie IP/Port oder Windows-Druckername müssen gültig sein.", True)
                PrinterManagerStatusText.Text = "Druckeränderung nicht gespeichert. Bitte Eingaben prüfen."
            End Try
        End Sub

        Private Function ReadPrinterEditor(existingId As String) As PrinterProfile
            Dim nameValue = PrinterNameBox.Text.Trim()
            Dim hostValue = PrinterIpBox.Text.Trim()
            Dim queueValue = PrinterQueueBox.Text.Trim()
            Dim selectedSupvan = TryCast(SupvanDeviceCombo.SelectedItem, SupvanUsbDevice)
            If nameValue = "" Then Throw New InvalidOperationException("Der Druckername fehlt.")
            If _printers.Any(Function(printer) Not String.Equals(printer.Id, existingId, StringComparison.OrdinalIgnoreCase) AndAlso String.Equals(printer.Name, nameValue, StringComparison.CurrentCultureIgnoreCase)) Then
                Throw New InvalidOperationException("Dieser Druckername ist bereits vorhanden.")
            End If
            Dim connectionItem = TryCast(PrinterConnectionModeCombo.SelectedItem, ComboBoxItem)
            Dim connectionMode = If(connectionItem Is Nothing, PrinterProfile.TcpRawConnection, Convert.ToString(connectionItem.Tag, CultureInfo.InvariantCulture))
            Dim languageItem = TryCast(PrinterLanguageCombo.SelectedItem, ComboBoxItem)
            Dim selectedLanguage = Convert.ToString(languageItem?.Tag, CultureInfo.InvariantCulture)
            If String.Equals(selectedLanguage, PrinterProfile.SupvanRasterLanguage, StringComparison.OrdinalIgnoreCase) Then
                connectionMode = PrinterProfile.SupvanUsbConnection
            End If
            Dim usesWindowsQueue = String.Equals(connectionMode, PrinterProfile.WindowsQueueConnection, StringComparison.OrdinalIgnoreCase)
            Dim usesSupvanUsb = String.Equals(connectionMode, PrinterProfile.SupvanUsbConnection, StringComparison.OrdinalIgnoreCase)
            If usesSupvanUsb AndAlso selectedSupvan Is Nothing AndAlso _supvanDevices.Count = 1 Then
                selectedSupvan = _supvanDevices(0)
                SupvanDeviceCombo.SelectedItem = selectedSupvan
            End If
            If usesWindowsQueue Then
                If queueValue = "" Then Throw New InvalidOperationException("Der Windows-Druckername oder UNC-Pfad fehlt.")
            ElseIf usesSupvanUsb Then
                If selectedSupvan Is Nothing Then Throw New InvalidOperationException("Bitte ein kompatibles SUPVAN-USB-Gerät auswählen.")
            ElseIf hostValue = "" Then
                Throw New InvalidOperationException("IP-Adresse oder Hostname fehlt.")
            End If
            Dim port As Integer = 9100
            If Not usesWindowsQueue AndAlso Not usesSupvanUsb AndAlso (Not Integer.TryParse(PrinterPortBox.Text, port) OrElse port < 1 OrElse port > 65535) Then Throw New InvalidOperationException("Der TCP-Port muss zwischen 1 und 65535 liegen.")
            Dim margin As Double
            If Not TryParseDouble(MarginBox.Text, margin) OrElse margin < 0 OrElse margin > 8 Then Throw New InvalidOperationException("Der Innenrand muss zwischen 0 und 8 mm liegen.")
            Dim language = If(languageItem Is Nothing, PrinterProfile.TsplLanguage, Convert.ToString(languageItem.Tag, CultureInfo.InvariantCulture))
            If usesSupvanUsb Then language = PrinterProfile.SupvanRasterLanguage
            If String.Equals(language, PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase) AndAlso Not usesWindowsQueue Then
                Throw New InvalidOperationException("Windows-Bilddruck benötigt den Druckweg ‚Windows-Drucker/Freigabe‘.")
            End If
            Dim dpiItem = TryCast(PrinterDpiCombo.SelectedItem, ComboBoxItem)
            Dim dpi As Integer = 203
            If dpiItem IsNot Nothing Then Integer.TryParse(Convert.ToString(dpiItem.Tag, CultureInfo.InvariantCulture), dpi)
            Dim preferredProfile = TryCast(PrinterProfileCombo.SelectedItem, LabelProfile)
            If preferredProfile Is Nothing Then Throw New InvalidOperationException("Das Standard-Etikett dieses Druckers fehlt.")
            Dim rotationItem = TryCast(RasterRotationCombo.SelectedItem, ComboBoxItem)
            Dim rotation As Integer
            If rotationItem IsNot Nothing Then Integer.TryParse(Convert.ToString(rotationItem.Tag, CultureInfo.InvariantCulture), rotation)
            Return New PrinterProfile With {
                .Id = existingId,
                .Name = nameValue,
                .ConnectionMode = connectionMode,
                .Host = hostValue,
                .Port = port,
                .QueueName = queueValue,
                .SupvanDeviceId = If(selectedSupvan?.TechnicalId, ""),
                .SupvanDevicePath = If(selectedSupvan?.DevicePath, ""),
                .SupvanSerialNumber = If(selectedSupvan?.SerialNumber, ""),
                .SupvanAlias = SupvanAliasBox.Text.Trim(),
                .SupvanVid = If(selectedSupvan?.Vid, 0),
                .SupvanPid = If(selectedSupvan?.Pid, 0),
                .SupvanManufacturer = If(selectedSupvan?.Manufacturer, ""),
                .SupvanProductName = If(selectedSupvan?.ProductName, ""),
                .Language = language,
                .Dpi = If(usesSupvanUsb, 203, dpi),
                .Density = CInt(Math.Round(DensitySlider.Value)),
                .Speed = CInt(Math.Round(SpeedSlider.Value)),
                .MarginMm = margin,
                .PreferredProfileId = preferredProfile.Id,
                .SwapLabelOrientation = SwapLabelOrientationCheck.IsChecked.GetValueOrDefault(),
                .RasterRotationDegrees = If(String.Equals(language, PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase), rotation, 0),
                .CutterEnabled = Not usesSupvanUsb AndAlso Not String.Equals(language, PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase) AndAlso CutterCheck.IsChecked.GetValueOrDefault()
            }
        End Function

        Private Sub PrinterConnectionModeChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isLoadingPrinterEditor Then Return
            Dim connectionItem = TryCast(PrinterConnectionModeCombo.SelectedItem, ComboBoxItem)
            Dim connectionMode = If(connectionItem Is Nothing, PrinterProfile.TcpRawConnection, Convert.ToString(connectionItem.Tag, CultureInfo.InvariantCulture))
            If String.Equals(connectionMode, PrinterProfile.SupvanUsbConnection, StringComparison.OrdinalIgnoreCase) Then
                SelectDirectSupvanMode()
            End If
            If String.Equals(connectionMode, PrinterProfile.TcpRawConnection, StringComparison.OrdinalIgnoreCase) AndAlso
               (String.Equals(ComboTag(PrinterLanguageCombo), PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase) OrElse
                String.Equals(ComboTag(PrinterLanguageCombo), PrinterProfile.SupvanRasterLanguage, StringComparison.OrdinalIgnoreCase)) Then
                SelectComboByTag(PrinterLanguageCombo, PrinterProfile.ZplLanguage)
            End If
            UpdatePrinterEditorVisibility()
        End Sub

        Private Sub PrinterLanguageChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isLoadingPrinterEditor Then Return
            Dim languageItem = TryCast(PrinterLanguageCombo.SelectedItem, ComboBoxItem)
            If String.Equals(Convert.ToString(languageItem?.Tag, CultureInfo.InvariantCulture), PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase) Then
                If LooksLikeSupvanProfile() Then
                    SelectDirectSupvanMode(SupvanDeviceCombo.SelectedItem Is Nothing)
                    SetStatus("SUPVAN wird direkt per USB/HID gesucht; kein Windows-Druckername nötig.", StatusKind.Neutral)
                    Return
                End If
                SelectComboByTag(PrinterConnectionModeCombo, PrinterProfile.WindowsQueueConnection)
            ElseIf String.Equals(Convert.ToString(languageItem?.Tag, CultureInfo.InvariantCulture), PrinterProfile.SupvanRasterLanguage, StringComparison.OrdinalIgnoreCase) Then
                SelectDirectSupvanMode(SupvanDeviceCombo.SelectedItem Is Nothing)
            End If
            UpdatePrinterEditorVisibility()
        End Sub

        Private Sub UpdatePrinterEditorVisibility()
            If PrinterConnectionModeCombo Is Nothing OrElse PrinterLanguageCombo Is Nothing Then Return
            Dim connectionItem = TryCast(PrinterConnectionModeCombo.SelectedItem, ComboBoxItem)
            Dim languageItem = TryCast(PrinterLanguageCombo.SelectedItem, ComboBoxItem)
            Dim usesWindowsQueue = String.Equals(Convert.ToString(connectionItem?.Tag, CultureInfo.InvariantCulture), PrinterProfile.WindowsQueueConnection, StringComparison.OrdinalIgnoreCase)
            Dim usesSupvanUsb = String.Equals(Convert.ToString(connectionItem?.Tag, CultureInfo.InvariantCulture), PrinterProfile.SupvanUsbConnection, StringComparison.OrdinalIgnoreCase)
            Dim usesRaster = String.Equals(Convert.ToString(languageItem?.Tag, CultureInfo.InvariantCulture), PrinterProfile.WindowsRasterLanguage, StringComparison.OrdinalIgnoreCase)
            TcpConnectionPanel.Visibility = If(usesWindowsQueue OrElse usesSupvanUsb, Visibility.Collapsed, Visibility.Visible)
            WindowsQueuePanel.Visibility = If(usesWindowsQueue, Visibility.Visible, Visibility.Collapsed)
            SupvanUsbPanel.Visibility = If(usesSupvanUsb, Visibility.Visible, Visibility.Collapsed)
            DensitySlider.IsEnabled = Not usesRaster AndAlso Not usesSupvanUsb
            SpeedSlider.IsEnabled = Not usesRaster AndAlso Not usesSupvanUsb
            CutterCheck.IsEnabled = Not usesRaster AndAlso Not usesSupvanUsb
            RasterRotationPanel.Visibility = If(usesRaster, Visibility.Visible, Visibility.Collapsed)
            If usesRaster OrElse usesSupvanUsb Then CutterCheck.IsChecked = False
            ConnectionTestButton.Content = If(usesWindowsQueue, "Warteschlange testen", If(usesSupvanUsb, "Synthetischen Testdruck prüfen", "Verbindung testen"))
        End Sub

        Private Sub RefreshSupvanDevicesClick(sender As Object, e As RoutedEventArgs)
            RefreshSupvanDevices(TryCast(SettingsPrinterList.SelectedItem, PrinterProfile))
        End Sub

        Private Sub SupvanDeviceSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If Not _isLoadingPrinterEditor AndAlso SupvanDeviceCombo.SelectedItem IsNot Nothing Then
                SelectDirectSupvanMode(False)
            End If
            UpdateSupvanDeviceDetails()
        End Sub

        Private Sub ChooseSharedTemplatePathClick(sender As Object, e As RoutedEventArgs)
            Dim dialog As New SaveFileDialog With {
                .Title = "Zentrale Etikettenvorlagendatei wählen",
                .Filter = "Vorlagen-JSON (*.json)|*.json|Alle Dateien (*.*)|*.*",
                .FileName = If(String.IsNullOrWhiteSpace(SharedTemplatePathBox.Text), "TSC-Etikettendruck-Vorlagen.json", Path.GetFileName(SharedTemplatePathBox.Text)),
                .AddExtension = True,
                .DefaultExt = ".json"
            }
            If Not String.IsNullOrWhiteSpace(SharedTemplatePathBox.Text) Then
                Dim directoryPath = Path.GetDirectoryName(SharedTemplatePathBox.Text)
                If Not String.IsNullOrWhiteSpace(directoryPath) AndAlso Directory.Exists(directoryPath) Then dialog.InitialDirectory = directoryPath
            End If
            If dialog.ShowDialog(Me) <> True Then Return
            SharedTemplatePathBox.Text = dialog.FileName
            SaveSharedTemplateSettings()
            ReloadTemplateStores()
            RefreshTemplatesForPrinter(_selectedTemplate?.Id)
            SetStatus("Zentraler Vorlagenpfad gesetzt.", StatusKind.Success)
        End Sub

        Private Sub ReloadTemplatesClick(sender As Object, e As RoutedEventArgs)
            SaveSharedTemplateSettings()
            ReloadTemplateStores()
            RefreshTemplatesForPrinter(_selectedTemplate?.Id)
            Dim message = If(HasSharedTemplateStore(), $"Vorlagen neu geladen: {_localTemplates.Count} lokal, {_sharedTemplates.Count} zentral.", $"Lokale Vorlagen neu geladen: {_localTemplates.Count}.")
            SetStatus(message, StatusKind.Success)
            If Not String.IsNullOrWhiteSpace(_templateLoadError) Then ShowInfo("Vorlagen teilweise nicht geladen", _templateLoadError, GetSharedTemplatePath(), True)
        End Sub

        Private Sub TemplateSaveTargetChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing Then Return
            Dim combo = TryCast(sender, ComboBox)
            Dim selected = TryCast(combo?.SelectedItem, ComboBoxItem)
            Dim target = NormalizeTemplateSaveTarget(Convert.ToString(selected?.Tag, CultureInfo.InvariantCulture))
            _settings.TemplateSaveTarget = target
            SelectComboByTag(TemplateSaveTargetCombo, target)
            SelectComboByTag(TemplateSaveTargetInlineCombo, target)
            SettingsStore.Save(_settings)
            UpdateSharedTemplateStatus()
        End Sub

        Private Sub SaveSharedTemplateSettings()
            _settings.SharedTemplatePath = SharedTemplatePathBox.Text.Trim()
            Dim selected = TryCast(TemplateSaveTargetCombo.SelectedItem, ComboBoxItem)
            _settings.TemplateSaveTarget = NormalizeTemplateSaveTarget(Convert.ToString(selected?.Tag, CultureInfo.InvariantCulture))
            If _settings.TemplateSaveTarget = "SHARED" AndAlso String.IsNullOrWhiteSpace(_settings.SharedTemplatePath) Then _settings.TemplateSaveTarget = "LOCAL"
            SelectComboByTag(TemplateSaveTargetCombo, _settings.TemplateSaveTarget)
            SelectComboByTag(TemplateSaveTargetInlineCombo, _settings.TemplateSaveTarget)
            SettingsStore.Save(_settings)
            UpdateSharedTemplateStatus()
        End Sub

        Private Async Sub SupvanTestPrintClick(sender As Object, e As RoutedEventArgs)
            Dim testPrinter As PrinterProfile = Nothing
            Try
                testPrinter = ReadPrinterEditor(If(TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)?.Id, ""))
                If Not testPrinter.UsesSupvanUsb Then
                    ShowInfo("Kein SUPVAN-Druckweg gewählt", "Bitte zuerst den Druckweg ‚SUPVAN-USB direkt‘ wählen.")
                    Return
                End If
                SupvanTestPrintButton.IsEnabled = False
                PrinterManagerStatusText.Text = $"Drucke synthetisches Testetikett auf {testPrinter.Name} · {testPrinter.ConnectionText}..."
                SetStatus("SUPVAN-Testetikett wird ohne Patientendaten gedruckt.", StatusKind.Busy)
                Using timeout As New CancellationTokenSource(TimeSpan.FromSeconds(30))
                    Dim result = Await SupvanUsbService.PrintSyntheticTestAsync(_settings, testPrinter, timeout.Token)
                    PrinterManagerStatusText.Text = $"SUPVAN-Testetikett gedruckt: {result.WidthMm} × {result.HeightMm} mm, {result.Frames} Datenframes."
                    SetStatus("SUPVAN-Testetikett gedruckt.", StatusKind.Success)
                End Using
            Catch ex As Exception
                ShowInfo("SUPVAN-Testdruck fehlgeschlagen", "Das synthetische Testetikett wurde nicht gedruckt.", ex.Message, True)
                PrinterManagerStatusText.Text = "SUPVAN-Testdruck fehlgeschlagen: " & ex.Message
                SetStatus("SUPVAN-Testdruck fehlgeschlagen.", StatusKind.ErrorState)
            Finally
                SupvanTestPrintButton.IsEnabled = True
            End Try
        End Sub

        Private Sub RefreshSupvanDevices(configured As PrinterProfile)
            If SupvanDeviceCombo Is Nothing Then Return
            Dim selectedBefore = TryCast(SupvanDeviceCombo.SelectedItem, SupvanUsbDevice)
            _supvanDevices.Clear()
            Dim devices As IReadOnlyList(Of SupvanUsbDevice)
            Try
                devices = SupvanUsbService.EnumerateCompatibleDevices()
            Catch ex As Exception
                SupvanDeviceDetailsText.Text = "SUPVAN-Gerätesuche fehlgeschlagen: " & ex.Message
                Return
            End Try
            For Each device In devices
                _supvanDevices.Add(device)
            Next

            Dim result = SupvanUsbService.ResolveSelection(configured, devices)
            If result.SelectedDevice IsNot Nothing Then
                SupvanDeviceCombo.SelectedItem = _supvanDevices.FirstOrDefault(Function(item) String.Equals(item.TechnicalId, result.SelectedDevice.TechnicalId, StringComparison.OrdinalIgnoreCase))
            ElseIf selectedBefore IsNot Nothing Then
                SupvanDeviceCombo.SelectedItem = _supvanDevices.FirstOrDefault(Function(item) String.Equals(item.TechnicalId, selectedBefore.TechnicalId, StringComparison.OrdinalIgnoreCase))
            Else
                SupvanDeviceCombo.SelectedItem = Nothing
            End If
            SupvanDeviceDetailsText.Text = result.Message
            UpdateSupvanDeviceDetails()
        End Sub

        Private Sub UpdateSupvanDeviceDetails()
            If SupvanDeviceDetailsText Is Nothing Then Return
            Dim device = TryCast(SupvanDeviceCombo.SelectedItem, SupvanUsbDevice)
            If device Is Nothing Then Return
            Dim serial = If(String.IsNullOrWhiteSpace(device.SerialNumber), "keine Seriennummer · Identität über Windows-Gerätepfad", "Seriennummer " & device.SerialNumber)
            SupvanDeviceDetailsText.Text = $"{device.DisplayName} · VID {device.Vid:X4} · PID {device.Pid:X4} · {serial} · Hersteller '{device.Manufacturer}' · Produkt '{device.ProductName}'"
        End Sub

        Private Sub ChooseWindowsPrinterClick(sender As Object, e As RoutedEventArgs)
            Dim dialog As New WindowsPrinterPickerWindow(PrinterQueueBox.Text) With {.Owner = Me}
            If dialog.ShowDialog().GetValueOrDefault() Then
                PrinterQueueBox.Text = dialog.SelectedQueueName
                Dim preset = WindowsPrinterPresetDetector.Detect(dialog.SelectedPrinterName, dialog.SelectedDriverName)
                If preset IsNot Nothing Then
                    SelectComboByTag(PrinterLanguageCombo, preset.Language)
                    SelectComboByTag(PrinterDpiCombo, preset.Dpi.ToString(CultureInfo.InvariantCulture))
                    Dim preferredProfile = _profiles.FirstOrDefault(Function(profile) profile.Id = preset.PreferredProfileId)
                    If preferredProfile IsNot Nothing Then PrinterProfileCombo.SelectedItem = preferredProfile
                    SwapLabelOrientationCheck.IsChecked = False
                    SelectComboByTag(RasterRotationCombo, preset.RasterRotationDegrees.ToString(CultureInfo.InvariantCulture))
                End If
                If String.IsNullOrWhiteSpace(PrinterNameBox.Text) Then PrinterNameBox.Text = dialog.SelectedPrinterName
                UpdatePrinterEditorVisibility()
                Dim presetText = If(preset Is Nothing, "", $" · {preset.Manufacturer}-Voreinstellung erkannt")
                PrinterManagerStatusText.Text = "Windows-Drucker ausgewählt: " & dialog.SelectedQueueName & presetText
                SetStatus("Windows-Drucker ausgewählt" & presetText & " – Änderungen noch übernehmen.", StatusKind.Neutral)
            End If
        End Sub

        Private Sub UseAsTargetPrinterClick(sender As Object, e As RoutedEventArgs)
            Dim selected = TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)
            If selected Is Nothing OrElse _editingNewPrinter Then
                ShowInfo("Drucker noch nicht gespeichert", "Bitte den neuen Drucker zuerst mit ‚Änderungen übernehmen‘ speichern.")
                Return
            End If
            TargetPrinterCombo.SelectedItem = selected
        End Sub

        Private Sub DeletePrinterClick(sender As Object, e As RoutedEventArgs)
            Dim selected = TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)
            If selected Is Nothing Then
                ShowInfo("Kein Drucker ausgewählt", "Bitte zuerst einen Drucker in der Liste auswählen.")
                Return
            End If
            If _printers.Count <= 1 Then
                ShowInfo("Letzter Drucker bleibt erhalten", "Mindestens ein Drucker muss eingerichtet bleiben.")
                Return
            End If
            If CurrentPrinter() Is selected Then
                ShowInfo("Zieldrucker kann nicht entfernt werden", "Bitte zuerst einen anderen Drucker auswählen und als Zieldrucker festlegen.")
                Return
            End If
            If Not Confirm("Drucker entfernen?", selected.Name, selected.ConnectionText, "Entfernen") Then Return
            Dim index = _printers.IndexOf(selected)
            _printers.Remove(selected)
            SettingsPrinterList.SelectedItem = _printers(Math.Max(0, Math.Min(index - 1, _printers.Count - 1)))
            SyncPrintersToSettings()
            SettingsStore.Save(_settings)
            SetStatus($"Drucker {selected.Name} entfernt.", StatusKind.Success)
        End Sub

        Private Sub SyncPrintersToSettings()
            _settings.Printers = _printers.Select(Function(printer) printer.Copy()).ToList()
            Dim current = CurrentPrinter()
            If current IsNot Nothing Then
                _settings.SelectedPrinterId = current.Id
                SyncLegacyPrinterFields(current)
            End If
        End Sub

        Private Sub SyncLegacyPrinterFields(printer As PrinterProfile)
            _settings.PrinterIp = printer.Host
            _settings.PrinterPort = printer.Port
            _settings.Dpi = printer.Dpi
            _settings.Density = printer.Density
            _settings.Speed = printer.Speed
            _settings.MarginMm = printer.MarginMm
            _settings.CutterEnabled = printer.CutterEnabled
        End Sub

        Private Sub LabelStageMouseDown(sender As Object, e As MouseButtonEventArgs)
            EnsureEditorEditable()
            LabelEditor.Focus()
        End Sub

        Private Sub EnsureEditorEditable()
            LabelEditor.IsReadOnly = False
            LabelEditor.IsDocumentEnabled = True
            LabelEditor.IsEnabled = True
            LabelEditor.IsHitTestVisible = True
            LabelEditor.Focusable = True
        End Sub

        Private Sub SetLabelDocument(document As FlowDocument)
            ClearImageFocus(False)
            _activeTemplate = Nothing
            LabelEditor.Document = If(document, LabelDocumentFactory.CreateBaseDocument())
            EnsureEditorEditable()
            UpdateLabelDimensions()
            ScheduleFitCheck()
            UpdateImageFocusControls()
        End Sub

        Private Sub WindowSizeChanged(sender As Object, e As SizeChangedEventArgs)
            UpdateResponsiveTemplateLayout()
        End Sub

        Private Sub UpdateResponsiveTemplateLayout()
            If TemplatePanel Is Nothing OrElse TemplatesCompactButton Is Nothing Then Return
            Dim effectiveWidth = If(ActualWidth > 0, ActualWidth, Width)
            Dim effectiveHeight = If(ActualHeight > 0, ActualHeight, Height)
            EditorArea.ColumnDefinitions(1).Width = New GridLength(If(effectiveWidth < 1500, 340, 420))
            TemplatePanel.Visibility = Visibility.Visible
            TemplatesCompactButton.Visibility = Visibility.Visible
            If TemplateDesignListRow IsNot Nothing Then TemplateDesignListRow.Height = New GridLength(If(effectiveHeight >= 850, 96, 52))
        End Sub

        Private Sub RefreshTemplatesForPrinter(Optional preferredId As String = "")
            If TemplateList Is Nothing OrElse CompactTemplateList Is Nothing Then Return
            TemplatePrinterText.Text = "Alle Vorlagen · bevorzugter Drucker wird beim Laden gewählt"
            CompactTemplatePrinterText.Text = "Alle Vorlagen"

            If String.IsNullOrWhiteSpace(preferredId) Then preferredId = _selectedTemplate?.Id
            _visibleTemplates.Clear()
            For Each entry In _allTemplates.OrderBy(Function(item) item.Name, StringComparer.CurrentCultureIgnoreCase)
                If String.IsNullOrWhiteSpace(entry.PrinterName) Then entry.PrinterName = _printers.FirstOrDefault(Function(item) String.Equals(item.Id, entry.PrinterId, StringComparison.OrdinalIgnoreCase))?.Name
                _visibleTemplates.Add(entry)
            Next
            Dim selected = _visibleTemplates.FirstOrDefault(Function(item) String.Equals(item.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            If selected Is Nothing Then selected = _visibleTemplates.FirstOrDefault()
            SetSelectedTemplate(selected)
            UpdateTemplatePasteButtons()
        End Sub

        Private Sub SetSelectedTemplate(entry As LabelTemplateDefinition)
            _selectedTemplate = entry
            _isSynchronizingTemplateSelection = True
            Try
                TemplateList.SelectedItem = entry
                CompactTemplateList.SelectedItem = entry
                TemplateDesignList.SelectedItem = entry
            Finally
                _isSynchronizingTemplateSelection = False
            End Try
        End Sub

        Private Sub TemplateSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isSynchronizingTemplateSelection Then Return
            SetSelectedTemplate(TryCast(TemplateList.SelectedItem, LabelTemplateDefinition))
        End Sub

        Private Sub CompactTemplateSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isSynchronizingTemplateSelection Then Return
            SetSelectedTemplate(TryCast(CompactTemplateList.SelectedItem, LabelTemplateDefinition))
        End Sub

        Private Sub TemplateDesignSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isSynchronizingTemplateSelection Then Return
            SetSelectedTemplate(TryCast(TemplateDesignList.SelectedItem, LabelTemplateDefinition))
        End Sub

        Private Sub TemplatesCompactButtonClick(sender As Object, e As RoutedEventArgs)
            RefreshTemplatesForPrinter(_selectedTemplate?.Id)
            TemplateCompactPopup.IsOpen = True
            SetStatus("Druckerspezifische Vorlagen geöffnet.", StatusKind.Neutral)
        End Sub

        Private Sub TemplateListDoubleClick(sender As Object, e As MouseButtonEventArgs)
            LoadSelectedTemplate()
        End Sub

        Private Sub LoadTemplateClick(sender As Object, e As RoutedEventArgs)
            LoadSelectedTemplate()
        End Sub

        Private Sub LoadSelectedTemplate()
            If _selectedTemplate Is Nothing Then
                SetStatus("Für diesen Drucker ist noch keine Vorlage ausgewählt.", StatusKind.Warning)
                Return
            End If
            Try
                Dim entry = _selectedTemplate
                _isApplyingTemplate = True
                Dim compatibleIds = If(entry.PrinterIds, New List(Of String)())
                Dim preferredPrinter = _printers.FirstOrDefault(Function(item) String.Equals(item.Id, entry.PrinterId, StringComparison.OrdinalIgnoreCase))
                If preferredPrinter Is Nothing Then preferredPrinter = _printers.FirstOrDefault(Function(item) compatibleIds.Any(Function(id) String.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase)))
                If preferredPrinter IsNot Nothing Then TargetPrinterCombo.SelectedItem = preferredPrinter
                Dim profile = _profiles.FirstOrDefault(Function(item) String.Equals(item.Id, entry.ProfileId, StringComparison.OrdinalIgnoreCase))
                If profile Is Nothing Then profile = _profiles.FirstOrDefault(Function(item) Math.Abs(item.WidthMm - entry.WidthMm) < 0.01 AndAlso Math.Abs(item.HeightMm - entry.HeightMm) < 0.01)
                If profile IsNot Nothing Then ProfileCombo.SelectedItem = profile
                SetLabelDocument(LabelTemplateStore.RestoreDocument(entry))
                _activeTemplate = entry
                SetSelectedTemplate(entry)
                _settings.LastUsedTemplateId = entry.Id
                If CurrentPrinter() IsNot Nothing Then _settings.SelectedPrinterId = CurrentPrinter().Id
                If CurrentBaseProfile() IsNot Nothing Then _settings.SelectedProfileId = CurrentBaseProfile().Id
                SettingsStore.Save(_settings)
                TemplateCompactPopup.IsOpen = False
                If preferredPrinter Is Nothing Then
                    SetStatus($"Vorlage {entry.Name} geladen · bevorzugter Drucker nicht vorhanden, aktueller Drucker bleibt gewählt.", StatusKind.Warning)
                Else
                    SetStatus($"Vorlage {entry.Name} geladen · {preferredPrinter.Name} · {entry.WidthMm:0.##} × {entry.HeightMm:0.##} mm.", StatusKind.Success)
                End If
            Catch ex As Exception
                ShowInfo("Vorlage konnte nicht geladen werden", ex.Message, _selectedTemplate.Name, True)
            Finally
                _isApplyingTemplate = False
            End Try
        End Sub

        Private Sub SaveTemplateClick(sender As Object, e As RoutedEventArgs)
            Dim sourceElement = TryCast(sender, FrameworkElement)
            Dim saveAsNew = String.Equals(Convert.ToString(sourceElement?.Tag), "SAVE_AS_NEW", StringComparison.Ordinal)
            Dim printer = CurrentPrinter()
            Dim profile = CurrentProfile()
            Dim baseProfile = CurrentBaseProfile()
            If printer Is Nothing OrElse profile Is Nothing OrElse baseProfile Is Nothing Then
                SetStatus("Drucker und Etikettenformat müssen vor dem Speichern gewählt sein.", StatusKind.Warning)
                Return
            End If

            Dim containsResolvedPatientData = _activeTemplate IsNot Nothing AndAlso
                Not LabelTemplateEngine.ContainsPlaceholders(LabelEditor.Document) AndAlso
                LabelTemplateEngine.ContainsPlaceholders(LabelTemplateStore.RestoreDocument(_activeTemplate))

            Dim baseName = If(_activeTemplate?.Name, If(_selectedTemplate?.Name, "Vorlage"))
            Dim initialName = If(saveAsNew, baseName.Trim() & " – Kopie", baseName)
            Dim nameValue = If(_activeTemplate?.Name, "")
            If saveAsNew OrElse _activeTemplate Is Nothing Then
            Dim dialog As New TextInputDialogWindow(
                "Etikettenvorlage speichern",
                "Aussagekräftiger Name für diese Vorlage",
                initialName,
                $"Drucker: {printer.Name} · Format: {profile.WidthMm:0.##} × {profile.HeightMm:0.##} mm") With {.Owner = Me}
            If dialog.ShowDialog() <> True Then Return
            nameValue = dialog.InputValue
            End If

            Try
                Dim requestedScope = If(_activeTemplate IsNot Nothing AndAlso Not saveAsNew,
                    NormalizeTemplateSaveTarget(_activeTemplate.StoreScope),
                    NormalizeTemplateSaveTarget(_settings.TemplateSaveTarget))
                If requestedScope = "SHARED" AndAlso Not HasSharedTemplateStore() Then Throw New InvalidOperationException("Bitte zuerst einen zentralen Vorlagenpfad eintragen.")
                Dim targetTemplates = If(requestedScope = "SHARED", _sharedTemplates, _localTemplates)
                Dim existing = targetTemplates.FirstOrDefault(Function(item) String.Equals(item.Name, nameValue, StringComparison.CurrentCultureIgnoreCase))
                Dim sameNameElsewhere = _allTemplates.FirstOrDefault(Function(item) Not String.Equals(item.StoreScope, requestedScope, StringComparison.OrdinalIgnoreCase) AndAlso String.Equals(item.Name, nameValue, StringComparison.CurrentCultureIgnoreCase))
                If existing IsNot Nothing Then
                    Dim warningText = If(containsResolvedPatientData,
                        "Die vorhandene Vorlage wird überschrieben. Das aktuelle Etikett enthält bereits eingesetzte Patientendaten und keine Patienten-Platzhalter mehr.",
                        "Die vorhandene Vorlage wird durch die bearbeitete Fassung ersetzt. Abbrechen lässt sie unverändert.")
                    If Not Confirm("Vorlage überschreiben?", nameValue, warningText, "Überschreiben") Then Return
                ElseIf sameNameElsewhere IsNot Nothing Then
                    Dim otherScope = If(String.Equals(sameNameElsewhere.StoreScope, "SHARED", StringComparison.OrdinalIgnoreCase), "zentral", "lokal")
                    If Not Confirm("Gleichnamige Vorlage in anderer Quelle", nameValue, $"Es gibt bereits eine {otherScope} gespeicherte Vorlage mit diesem Namen. Die neue Vorlage wird zusätzlich im gewählten Speicherort angelegt.", "Zusätzlich speichern") Then Return
                ElseIf containsResolvedPatientData AndAlso
                       Not Confirm("Vorlage ohne Platzhalter speichern?", nameValue, "Das aktuelle Etikett enthält bereits eingesetzte Patientendaten und keine Patienten-Platzhalter mehr.", "Trotzdem speichern") Then
                    Return
                End If
                Dim overwritingExisting = existing IsNot Nothing

                Dim entry = If(existing, New LabelTemplateDefinition With {.Id = Guid.NewGuid().ToString("N")})
                Dim selectedPrinters = TemplatePrinterSelectionList.SelectedItems.Cast(Of PrinterProfile)().ToList()
                If selectedPrinters.Count = 0 Then selectedPrinters.Add(printer)
                If Not selectedPrinters.Any(Function(item) String.Equals(item.Id, printer.Id, StringComparison.OrdinalIgnoreCase)) Then selectedPrinters.Insert(0, printer)
                entry.Name = nameValue
                entry.PrinterId = printer.Id
                entry.PrinterName = printer.Name
                entry.PrinterIds = selectedPrinters.Select(Function(item) item.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                entry.PrinterNames = selectedPrinters.Select(Function(item) item.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList()
                entry.ProfileId = baseProfile.Id
                entry.ProfileName = baseProfile.Name
                entry.WidthMm = profile.WidthMm
                entry.HeightMm = profile.HeightMm
                entry.DocumentXaml = DocumentImageCodec.Serialize(LabelEditor.Document)
                entry.UpdatedAt = DateTimeOffset.Now
                entry.StoreScope = requestedScope
                If existing Is Nothing Then targetTemplates.Add(entry)
                targetTemplates.RemoveAll(Function(item) Not ReferenceEquals(item, entry) AndAlso String.Equals(item.Name.Trim(), nameValue.Trim(), StringComparison.CurrentCultureIgnoreCase))
                PersistTemplatesForScope(requestedScope)
                _activeTemplate = entry
                _settings.LastUsedTemplateId = entry.Id
                _settings.SelectedPrinterId = printer.Id
                _settings.SelectedProfileId = baseProfile.Id
                SettingsStore.Save(_settings)
                RefreshTemplatesForPrinter(entry.Id)
                Dim resultText = If(overwritingExisting,
                    $"Vorlage überschrieben: {entry.Name} · {If(requestedScope = "SHARED", "zentral", "lokal")} · {printer.Name}.",
                    If(saveAsNew, $"Neue Vorlage {If(requestedScope = "SHARED", "zentral", "lokal")} gespeichert: {entry.Name} · Original unverändert.", $"Vorlage {If(requestedScope = "SHARED", "zentral", "lokal")} gespeichert: {entry.Name} · {printer.Name}."))
                SetStatus(resultText, StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Vorlage nicht gespeichert", ex.Message, If(HasSharedTemplateStore(), GetSharedTemplatePath(), LabelTemplateStore.DefaultPath), True)
            End Try
        End Sub

        Private Sub DeleteTemplateClick(sender As Object, e As RoutedEventArgs)
            If _selectedTemplate Is Nothing Then
                SetStatus("Bitte zuerst eine Vorlage auswählen.", StatusKind.Warning)
                Return
            End If
            Dim entry = _selectedTemplate
            If Not Confirm("Vorlage löschen?", entry.Name, "Das aktuelle Etikett im Editor bleibt unverändert.", "Löschen") Then Return
            Try
                Dim scope = NormalizeTemplateSaveTarget(entry.StoreScope)
                If scope = "SHARED" Then
                    _sharedTemplates.Remove(entry)
                Else
                    _localTemplates.Remove(entry)
                End If
                If ReferenceEquals(_activeTemplate, entry) Then _activeTemplate = Nothing
                PersistTemplatesForScope(scope)
                RefreshTemplatesForPrinter()
                SetStatus($"Vorlage gelöscht: {entry.Name} · {If(scope = "SHARED", "zentral", "lokal")}.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Vorlage nicht gelöscht", ex.Message, If(HasSharedTemplateStore(), GetSharedTemplatePath(), LabelTemplateStore.DefaultPath), True)
            End Try
        End Sub

        Private Sub ExportTemplatesClick(sender As Object, e As RoutedEventArgs)
            If _allTemplates.Count = 0 Then
                SetStatus("Es sind noch keine Etikettenvorlagen zum Exportieren vorhanden.", StatusKind.Warning)
                Return
            End If
            Dim dialog As New SaveFileDialog With {
                .Title = "Alle Etikettenvorlagen exportieren",
                .Filter = "Etikettenstudio-Paket (*.zip)|*.zip|Vorlagen-JSON (*.json)|*.json",
                .FileName = $"Etikettenstudio-Vorlagen-{DateTime.Now:yyyyMMdd}.zip",
                .AddExtension = True,
                .DefaultExt = ".zip"
            }
            If dialog.ShowDialog(Me) <> True Then Return
            Try
                LabelTemplateStore.SavePackage(_allTemplates, dialog.FileName)
                SetStatus($"{_allTemplates.Count} Vorlage(n) exportiert: {Path.GetFileName(dialog.FileName)}", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Vorlagen nicht exportiert", ex.Message, dialog.FileName, True)
            End Try
        End Sub

        Private Sub ImportTemplatesClick(sender As Object, e As RoutedEventArgs)
            Dim dialog As New OpenFileDialog With {
                .Title = "Etikettenvorlagen importieren",
                .Filter = "Etikettenstudio-Paket (*.zip)|*.zip|Vorlagen-JSON (*.json)|*.json|Alle Dateien (*.*)|*.*",
                .CheckFileExists = True
            }
            If dialog.ShowDialog(Me) <> True Then Return
            Try
                Dim imported = LabelTemplateStore.LoadPackage(dialog.FileName)
                If imported.Count = 0 Then
                    ShowInfo("Keine Vorlagen gefunden", "Die ausgewählte Datei enthält keine verwendbaren Etikettenvorlagen.", dialog.FileName, True)
                    Return
                End If
                Dim targetScope = NormalizeTemplateSaveTarget(_settings.TemplateSaveTarget)
                If targetScope = "SHARED" AndAlso Not HasSharedTemplateStore() Then Throw New InvalidOperationException("Bitte zuerst einen zentralen Vorlagenpfad eintragen.")
                Dim targetTemplates = If(targetScope = "SHARED", _sharedTemplates, _localTemplates)
                Dim collisions = imported.Where(Function(incoming) targetTemplates.Any(Function(local) String.Equals(local.Name.Trim(), incoming.Name.Trim(), StringComparison.CurrentCultureIgnoreCase))).Count()
                Dim context = If(collisions = 0,
                    $"Bestehende Vorlagen bleiben erhalten. Importziel: {If(targetScope = "SHARED", "zentral", "lokal")}.",
                    $"{collisions} gleichnamige Vorlage(n) im Ziel werden ersetzt; lokale und zentrale Namen dürfen parallel existieren.")
                If Not Confirm("Vorlagen importieren?", $"{imported.Count} Vorlage(n) aus {Path.GetFileName(dialog.FileName)}", context, "Importieren") Then Return

                For Each incoming In imported
                    targetTemplates.RemoveAll(Function(local) String.Equals(local.Id, incoming.Id, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(local.Name.Trim(), incoming.Name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                    If String.IsNullOrWhiteSpace(incoming.Id) Then incoming.Id = Guid.NewGuid().ToString("N")
                    incoming.StoreScope = targetScope
                    targetTemplates.Add(incoming)
                Next
                PersistTemplatesForScope(targetScope)
                RefreshTemplatesForPrinter()
                SetStatus($"{imported.Count} Vorlage(n) {If(targetScope = "SHARED", "zentral", "lokal")} importiert · {collisions} gleichnamige ersetzt.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Vorlagen nicht importiert", ex.Message, "Die bestehende Vorlagensammlung wurde nicht verändert.", True)
            End Try
        End Sub

        Private Sub CopyTemplateClick(sender As Object, e As RoutedEventArgs)
            If _selectedTemplate Is Nothing Then
                SetStatus("Bitte zuerst eine gespeicherte Vorlage auswählen.", StatusKind.Warning)
                Return
            End If
            _templateClipboardXaml = _selectedTemplate.DocumentXaml
            UpdateTemplatePasteButtons()
            SetStatus($"Vorlage intern kopiert: {_selectedTemplate.Name}. Jetzt Drucker/Format wechseln und Paste wählen.", StatusKind.Success)
        End Sub

        Private Sub PasteTemplateClick(sender As Object, e As RoutedEventArgs)
            If String.IsNullOrWhiteSpace(_templateClipboardXaml) Then
                UpdateTemplatePasteButtons()
                SetStatus("Paste ist erst nach Copy einer Vorlage verfügbar.", StatusKind.Warning)
                Return
            End If
            Try
                SetLabelDocument(DocumentImageCodec.Deserialize(_templateClipboardXaml))
                TemplateCompactPopup.IsOpen = False
                SetStatus("Vorlageninhalt in das aktuelle Drucker-/Etikettenformat eingefügt – jetzt nachformatieren und neu speichern.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Vorlage nicht eingefügt", ex.Message, "Der interne Kopierspeicher wurde nicht verändert.", True)
            End Try
        End Sub

        Private Sub UpdateTemplatePasteButtons()
            Dim enabled = Not String.IsNullOrWhiteSpace(_templateClipboardXaml)
            If CompactTemplatePasteButton IsNot Nothing Then CompactTemplatePasteButton.IsEnabled = enabled
        End Sub

        Private Sub NewPlaceholderTemplateClick(sender As Object, e As RoutedEventArgs)
            If Not Confirm("Patienten-Grundvorlage einsetzen?", "Der aktuelle Editorinhalt wird durch formatierte Platzhalter ersetzt.", "%Name% · %Versicherungsnummer% · %Adresse% · %Telefon%", "Einsetzen") Then Return
            SetLabelDocument(LabelDocumentFactory.CreatePatientPlaceholderTemplate())
            TemplateCompactPopup.IsOpen = False
            RootTabs.SelectedIndex = 0
            SetStatus("Patienten-Platzhalter eingesetzt – Logo und Layout ergänzen, danach als Vorlage speichern.", StatusKind.Success)
        End Sub

        Private Sub TemplateDesignModeChanged(sender As Object, e As RoutedEventArgs)
            If TemplateDesignToggle Is Nothing OrElse PatientImportPanel Is Nothing Then Return
            _isTemplateDesignMode = TemplateDesignToggle.IsChecked.GetValueOrDefault()
            PatientImportPanel.Visibility = If(_isTemplateDesignMode, Visibility.Collapsed, Visibility.Visible)
            TemplateDesignPanel.Visibility = If(_isTemplateDesignMode, Visibility.Visible, Visibility.Collapsed)
            PrintBar.Visibility = If(_isTemplateDesignMode, Visibility.Collapsed, Visibility.Visible)
            TemplateSaveBar.Visibility = If(_isTemplateDesignMode, Visibility.Visible, Visibility.Collapsed)
            TemplateDesignToggle.Content = If(_isTemplateDesignMode, "✓ Vorlagen erstellen", "Vorlagen erstellen")
            PreviewTitleText.Text = If(_isTemplateDesignMode, "Vorlage gestalten", "Druckvorschau und Texteingabe")
            PreviewSubtitleText.Text = If(_isTemplateDesignMode, "Platzhalter und Formatierung werden gemeinsam gespeichert.", "Text direkt markieren und formatieren. Die weiße Fläche entspricht dem Etikett.")
            If _isTemplateDesignMode Then
                SelectTemplatePrinters(_activeTemplate)
                SetStatus("Vorlagenmodus aktiv · Patientenfelder rechts an der Cursorposition einfügen · BrainOffice-Autoimport pausiert.", StatusKind.Success)
            Else
                SetStatus("Etikett bearbeiten · bevorzugter Vorlagendrucker kann jederzeit geändert werden.", StatusKind.Neutral)
            End If
            EnsureEditorEditable()
        End Sub

        Private Sub TemplatePrinterPickerClick(sender As Object, e As RoutedEventArgs)
            SelectTemplatePrinters(_activeTemplate)
            TemplatePrinterPopup.IsOpen = True
        End Sub

        Private Sub CloseTemplatePrinterPickerClick(sender As Object, e As RoutedEventArgs)
            TemplatePrinterPopup.IsOpen = False
            Dim count = TemplatePrinterSelectionList.SelectedItems.Count
            TemplatePrinterPickerButton.Content = If(count = 1, "1 Drucker ausgewählt", $"{count} Drucker ausgewählt")
        End Sub

        Private Sub SelectTemplatePrinters(entry As LabelTemplateDefinition)
            If TemplatePrinterSelectionList Is Nothing Then Return
            TemplatePrinterSelectionList.SelectedItems.Clear()
            Dim ids = If(entry?.PrinterIds, New List(Of String)())
            If ids.Count = 0 AndAlso entry IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(entry.PrinterId) Then ids.Add(entry.PrinterId)
            For Each printer In _printers
                If ids.Any(Function(id) String.Equals(id, printer.Id, StringComparison.OrdinalIgnoreCase)) Then TemplatePrinterSelectionList.SelectedItems.Add(printer)
            Next
            If TemplatePrinterSelectionList.SelectedItems.Count = 0 AndAlso CurrentPrinter() IsNot Nothing Then TemplatePrinterSelectionList.SelectedItems.Add(CurrentPrinter())
            Dim count = TemplatePrinterSelectionList.SelectedItems.Count
            TemplatePrinterPickerButton.Content = If(count = 1, "1 Drucker ausgewählt", $"{count} Drucker ausgewählt")
        End Sub

        Private Sub InsertPlaceholderClick(sender As Object, e As RoutedEventArgs)
            Dim sourceElement = TryCast(sender, FrameworkElement)
            Dim token = If(sourceElement Is Nothing, "", Convert.ToString(sourceElement.Tag))
            If String.IsNullOrWhiteSpace(token) Then Return
            EnsureEditorEditable()
            Dim selection = LabelEditor.Selection
            selection.Text = token
            LabelEditor.CaretPosition = selection.End
            LabelEditor.Focus()
            ScheduleFitCheck()
            SetStatus($"{token} an der Cursorposition eingefügt.", StatusKind.Success)
        End Sub

        Private Sub UpdateLabelDimensions()
            Dim profile = CurrentProfile()
            Dim printer = CurrentPrinter()
            If profile Is Nothing OrElse printer Is Nothing Then Return
            Dim widthDip = profile.WidthMm / 25.4 * 96.0
            Dim heightDip = profile.HeightMm / 25.4 * 96.0
            LabelCanvas.Width = widthDip
            LabelCanvas.Height = heightDip
            LabelEditor.Padding = New Thickness(0)
            LabelRenderer.ApplyPageLayout(LabelEditor.Document, profile, printer.MarginMm)

            Dim widthDots = CInt(Math.Round(profile.WidthMm / 25.4 * printer.Dpi))
            Dim heightDots = CInt(Math.Round(profile.HeightMm / 25.4 * printer.Dpi))
            PixelInfoText.Text = $"{widthDots} × {heightDots} Druckpunkte"
            UpdatePrinterSummary()
        End Sub

        Private Sub FontFamilyChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse _isSynchronizingFormatControls OrElse LabelEditor Is Nothing Then Return
            Dim familyName = TryCast(FontFamilyCombo.SelectedItem, String)
            If String.IsNullOrWhiteSpace(familyName) Then familyName = FontFamilyCombo.Text
            If String.IsNullOrWhiteSpace(familyName) Then Return
            LabelEditor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, New FontFamily(familyName))
            LabelEditor.Focus()
            ScheduleFitCheck()
        End Sub

        Private Sub FontSizeChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse _isSynchronizingFormatControls OrElse LabelEditor Is Nothing Then Return
            If FontSizeCombo.IsDropDownOpen Then Return
            Dim selectedText = If(e.AddedItems.Count > 0, Convert.ToString(e.AddedItems(0), CultureInfo.CurrentCulture), FontSizeCombo.Text)
            ApplyFontSizeFromControl(selectedText)
        End Sub

        Private Sub FontSizeDropDownClosed(sender As Object, e As EventArgs)
            ApplyFontSizeFromControl()
        End Sub

        Private Sub FontSizeKeyDown(sender As Object, e As KeyEventArgs)
            If e.Key <> Key.Enter Then Return
            ApplyFontSizeFromControl()
            e.Handled = True
        End Sub

        Private Sub DecreaseFontSizeClick(sender As Object, e As RoutedEventArgs)
            AdjustFontSize(-0.5)
        End Sub

        Private Sub IncreaseFontSizeClick(sender As Object, e As RoutedEventArgs)
            AdjustFontSize(0.5)
        End Sub

        Private Sub AdjustFontSize(delta As Double)
            Dim currentPoints As Double
            Dim sizeValue = LabelEditor.Selection.GetPropertyValue(TextElement.FontSizeProperty)
            If sizeValue IsNot DependencyProperty.UnsetValue AndAlso TypeOf sizeValue Is Double Then
                currentPoints = LabelDocumentFactory.DipToPoints(CDbl(sizeValue))
            ElseIf Not TryParseDouble(FontSizeCombo.Text, currentPoints) Then
                currentPoints = 11
            End If
            currentPoints = Math.Clamp(currentPoints + delta, 4, 72)
            FontSizeCombo.Text = currentPoints.ToString("0.#", CultureInfo.CurrentCulture)
            ApplyFontSizeFromControl(FontSizeCombo.Text)
        End Sub

        Private Sub ApplyFontSizeFromControl(Optional requestedText As String = Nothing)
            Dim value As Double
            Dim textValue = If(requestedText, FontSizeCombo.Text)
            If Not TryParseDouble(textValue, value) OrElse value < 4 OrElse value > 72 Then Return
            LabelEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, LabelDocumentFactory.PointsToDip(value))
            LabelEditor.Focus()
            ScheduleFitCheck()
            SetStatus($"Schriftgröße {value:0.#} pt angewendet.", StatusKind.Success)
        End Sub

        Private Sub BoldClick(sender As Object, e As RoutedEventArgs)
            EditingCommands.ToggleBold.Execute(Nothing, LabelEditor)
            LabelEditor.Focus()
            ScheduleFitCheck()
        End Sub

        Private Sub ItalicClick(sender As Object, e As RoutedEventArgs)
            EditingCommands.ToggleItalic.Execute(Nothing, LabelEditor)
            LabelEditor.Focus()
            ScheduleFitCheck()
        End Sub

        Private Sub InsertLineClick(sender As Object, e As RoutedEventArgs)
            Dim currentParagraph = LabelEditor.Selection.Start.Paragraph
            Dim lineBlock = LabelDocumentFactory.CreateSeparatorBlock()
            Dim nextParagraph As New Paragraph(New Run("")) With {.Margin = New Thickness(0)}
            If currentParagraph IsNot Nothing AndAlso TypeOf currentParagraph.Parent Is FlowDocument Then
                LabelEditor.Document.Blocks.InsertAfter(currentParagraph, lineBlock)
                LabelEditor.Document.Blocks.InsertAfter(lineBlock, nextParagraph)
            Else
                LabelEditor.Document.Blocks.Add(lineBlock)
                LabelEditor.Document.Blocks.Add(nextParagraph)
            End If
            LabelEditor.CaretPosition = nextParagraph.ContentStart
            LabelEditor.Focus()
            ScheduleFitCheck()
            SetStatus("Trennlinie eingefügt.", StatusKind.Success)
        End Sub

        Private Sub InsertImageClick(sender As Object, e As RoutedEventArgs)
            Dim dialog As New OpenFileDialog With {
                .Title = "Logo oder Bild für das Etikett auswählen",
                .Filter = "Bilddateien (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg",
                .CheckFileExists = True
            }
            If dialog.ShowDialog(Me) <> True Then Return
            Try
                Dim bitmap = LoadEmbeddedBitmap(dialog.FileName)
                Dim mode = SelectedComboTag(ImageModeCombo, "INLINE")
                If mode = "BACKGROUND" Then
                    ApplyBackgroundImage(bitmap)
                    _selectedImage = Nothing
                    _backgroundImageSelected = True
                    UpdateImageFocusControls()
                Else
                    Dim image = CreateDocumentImage(bitmap)
                    InsertDocumentImage(image, mode)
                    SelectDocumentImage(image)
                End If
                ScheduleFitCheck()
                SetStatus($"Bild eingebettet: {Path.GetFileName(dialog.FileName)} · {ImageWidthBox.Text} mm.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Bild konnte nicht eingefügt werden", ex.Message, "Unterstützt werden PNG und JPG.", True)
            End Try
        End Sub

        Private Shared Function LoadEmbeddedBitmap(pathValue As String) As BitmapSource
            Dim bitmap As New BitmapImage()
            Using stream = File.OpenRead(pathValue)
                bitmap.BeginInit()
                bitmap.CacheOption = BitmapCacheOption.OnLoad
                bitmap.StreamSource = stream
                bitmap.EndInit()
            End Using
            bitmap.Freeze()
            Return bitmap
        End Function

        Private Function CreateDocumentImage(source As BitmapSource) As Image
            Dim widthDip = ReadImageWidthDip()
            Dim image As New Image With {
                .Source = source,
                .Width = widthDip,
                .Stretch = Stretch.Uniform,
                .Opacity = ReadImageOpacity(),
                .HorizontalAlignment = ReadImageHorizontalAlignment()
            }
            Return image
        End Function

        Private Sub InsertDocumentImage(image As Image, mode As String)
            Select Case mode
                Case "BLOCK"
                    Dim container As New BlockUIContainer(image) With {.Margin = New Thickness(0)}
                    Dim paragraph = LabelEditor.CaretPosition.Paragraph
                    If paragraph IsNot Nothing AndAlso TypeOf paragraph.Parent Is FlowDocument Then
                        LabelEditor.Document.Blocks.InsertBefore(paragraph, container)
                    Else
                        LabelEditor.Document.Blocks.Add(container)
                    End If
                Case "WRAP_LEFT", "WRAP_RIGHT"
                    Dim container As New BlockUIContainer(image) With {.Margin = New Thickness(0)}
                    Dim figure As New Figure(container, LabelEditor.CaretPosition) With {
                        .Width = New FigureLength(image.Width),
                        .Padding = New Thickness(0),
                        .HorizontalAnchor = If(mode = "WRAP_LEFT", FigureHorizontalAnchor.ContentLeft, FigureHorizontalAnchor.ContentRight),
                        .WrapDirection = If(mode = "WRAP_LEFT", WrapDirection.Right, WrapDirection.Left)
                    }
                Case Else
                    Dim ignored = New InlineUIContainer(image, LabelEditor.CaretPosition)
            End Select
            LabelEditor.Focus()
        End Sub

        Private Sub LabelEditorPreviewMouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
            Dim image = FindImageAncestor(TryCast(e.OriginalSource, DependencyObject))
            If image Is Nothing Then Return

            SelectDocumentImage(image)
            ' Das Ereignis bewusst nicht als behandelt markieren: RichTextBox darf
            ' Cursor und Auswahl normal verwalten, die Bildreferenz bleibt separat erhalten.
        End Sub

        Private Shared Function FindImageAncestor(source As DependencyObject) As Image
            Dim current = source
            While current IsNot Nothing
                Dim image = TryCast(current, Image)
                If image IsNot Nothing Then Return image

                Dim parent As DependencyObject = Nothing
                If TypeOf current Is Visual OrElse TypeOf current Is Media3D.Visual3D Then
                    parent = VisualTreeHelper.GetParent(current)
                End If
                If parent Is Nothing Then parent = LogicalTreeHelper.GetParent(current)
                current = parent
            End While
            Return Nothing
        End Function

        Private Sub SelectDocumentImage(image As Image)
            RemoveImageAdorner()
            _selectedImage = image
            _backgroundImageSelected = False

            Dim widthDip = image.Width
            If Double.IsNaN(widthDip) OrElse Double.IsInfinity(widthDip) OrElse widthDip <= 0 Then widthDip = image.ActualWidth
            If (Double.IsNaN(widthDip) OrElse Double.IsInfinity(widthDip) OrElse widthDip <= 0) AndAlso TypeOf image.Source Is BitmapSource Then
                widthDip = DirectCast(image.Source, BitmapSource).Width
            End If
            If Double.IsNaN(widthDip) OrElse Double.IsInfinity(widthDip) OrElse widthDip <= 0 Then widthDip = 20.0 / 25.4 * 96.0
            Dim widthMm = widthDip / 96.0 * 25.4
            If widthMm > 260 Then
                widthMm = 100
                widthDip = widthMm / 25.4 * 96.0
                image.Height = Double.NaN
                SetStatus("Sehr großes Clipboard-Bild automatisch auf 100 mm Ausgangsbreite verkleinert.", StatusKind.Neutral)
            Else
                SetStatus("Logo ausgewählt – die Auswahl bleibt beim Bedienen der Bildwerkzeuge erhalten.", StatusKind.Neutral)
            End If
            image.Width = widthDip

            ImageWidthBox.Text = widthMm.ToString("0.#", CultureInfo.CurrentCulture)
            SetImageOpacityControl(image.Opacity)
            SelectComboByTag(ImageAlignmentCombo, ImageAlignmentTag(image.HorizontalAlignment))
            SelectComboByTag(ImageModeCombo, ImageModeTag(image))
            UpdateImageFocusControls()
            AttachImageAdornerDeferred(image)
        End Sub

        Private Function DocumentImages() As List(Of Image)
            If LabelEditor?.Document Is Nothing Then Return New List(Of Image)()
            Return DocumentImageCodec.EnumerateImages(LabelEditor.Document).ToList()
        End Function

        Private Function HasBackgroundImage() As Boolean
            Dim brush = TryCast(LabelEditor?.Document?.Background, ImageBrush)
            Return brush IsNot Nothing AndAlso brush.ImageSource IsNot Nothing
        End Function

        Private Sub AttachImageAdornerDeferred(image As Image)
            Dispatcher.BeginInvoke(
                Sub()
                    If Not ReferenceEquals(_selectedImage, image) OrElse _backgroundImageSelected Then Return
                    RemoveImageAdorner()
                    LabelEditor.UpdateLayout()
                    Dim layer = AdornerLayer.GetAdornerLayer(image)
                    If layer Is Nothing Then Return
                    _selectedImageAdornerLayer = layer
                    _selectedImageAdorner = New SelectedImageAdorner(image)
                    layer.Add(_selectedImageAdorner)
                End Sub,
                DispatcherPriority.Loaded)
        End Sub

        Private Sub RemoveImageAdorner()
            If _selectedImageAdornerLayer IsNot Nothing AndAlso _selectedImageAdorner IsNot Nothing Then
                _selectedImageAdornerLayer.Remove(_selectedImageAdorner)
            End If
            _selectedImageAdorner = Nothing
            _selectedImageAdornerLayer = Nothing
        End Sub

        Private Sub ClearImageFocus(updateControls As Boolean)
            RemoveImageAdorner()
            _selectedImage = Nothing
            _backgroundImageSelected = False
            If updateControls Then UpdateImageFocusControls()
        End Sub

        Private Sub SelectBackgroundImage()
            Dim brush = TryCast(LabelEditor.Document.Background, ImageBrush)
            If brush Is Nothing OrElse brush.ImageSource Is Nothing Then Return
            RemoveImageAdorner()
            _selectedImage = Nothing
            _backgroundImageSelected = True
            SelectComboByTag(ImageModeCombo, "BACKGROUND")
            ImageWidthBox.Text = (brush.Viewport.Width / 96.0 * 25.4).ToString("0.#", CultureInfo.CurrentCulture)
            SetImageOpacityControl(brush.Opacity)
            UpdateImageFocusControls()
            SetStatus("Hintergrundgrafik als dauerhaftes Bearbeitungsziel ausgewählt.", StatusKind.Neutral)
        End Sub

        Private Sub UpdateImageFocusControls()
            If ImageFocusToggle Is Nothing Then Return
            Dim images = DocumentImages()
            Dim hasBackground = HasBackgroundImage()
            Dim total = images.Count + If(hasBackground, 1, 0)
            Dim selectedIndex = If(_selectedImage Is Nothing, -1, images.IndexOf(_selectedImage))
            If _backgroundImageSelected AndAlso hasBackground Then selectedIndex = images.Count

            If selectedIndex >= 0 Then
                ImageFocusToggle.IsChecked = True
                ImageFocusToggle.Content = If(_backgroundImageSelected,
                    $"Hintergrund {selectedIndex + 1}/{total} aktiv",
                    $"Bild {selectedIndex + 1}/{total} aktiv")
            Else
                ImageFocusToggle.IsChecked = False
                ImageFocusToggle.Content = If(total = 0, "Kein Bild vorhanden", $"Bildfokus aus · {total} vorhanden")
            End If
            PreviousImageButton.IsEnabled = total > 0
            NextImageButton.IsEnabled = total > 0
            DecreaseImageSizeButton.IsEnabled = selectedIndex >= 0
            IncreaseImageSizeButton.IsEnabled = selectedIndex >= 0
            If ImageDesignRowPanel IsNot Nothing AndAlso ImagePositionRowPanel IsNot Nothing Then
                ImagePositionRowPanel.Visibility = If(selectedIndex >= 0, Visibility.Visible, Visibility.Collapsed)
                ImageDesignRowPanel.Visibility = If(selectedIndex >= 0, Visibility.Collapsed, Visibility.Visible)
            End If
            UpdateBackgroundSelectionOverlay()
            UpdateImageOffsetText()
        End Sub

        Private Sub ShowImageDesignClick(sender As Object, e As RoutedEventArgs)
            ImagePositionRowPanel.Visibility = Visibility.Collapsed
            ImageDesignRowPanel.Visibility = Visibility.Visible
        End Sub

        Private Sub UpdateBackgroundSelectionOverlay()
            If BackgroundSelectionBorder Is Nothing Then Return
            If Not _backgroundImageSelected Then
                BackgroundSelectionBorder.Visibility = Visibility.Collapsed
                Return
            End If
            Dim brush = TryCast(LabelEditor.Document.Background, ImageBrush)
            If brush Is Nothing OrElse brush.Viewport.Width <= 0 OrElse brush.Viewport.Height <= 0 Then
                BackgroundSelectionBorder.Visibility = Visibility.Collapsed
                Return
            End If
            BackgroundSelectionBorder.Width = brush.Viewport.Width
            BackgroundSelectionBorder.Height = brush.Viewport.Height
            Canvas.SetLeft(BackgroundSelectionBorder, brush.Viewport.X)
            Canvas.SetTop(BackgroundSelectionBorder, brush.Viewport.Y)
            BackgroundSelectionBorder.Visibility = Visibility.Visible
        End Sub

        Private Sub UpdateImageOffsetText()
            If ImageOffsetText Is Nothing Then Return
            Dim xDip As Double
            Dim yDip As Double
            If _backgroundImageSelected Then
                Dim brush = TryCast(LabelEditor.Document.Background, ImageBrush)
                If brush IsNot Nothing Then
                    xDip = brush.Viewport.X
                    yDip = brush.Viewport.Y
                End If
            ElseIf _selectedImage IsNot Nothing Then
                Dim translate = TryCast(_selectedImage.RenderTransform, TranslateTransform)
                If translate IsNot Nothing Then
                    xDip = translate.X
                    yDip = translate.Y
                End If
            End If
            ImageOffsetText.Text = $"X {LabelDocumentFactory.DipToPoints(xDip):0.#} · Y {LabelDocumentFactory.DipToPoints(yDip):0.#} pt"
        End Sub

        Private Sub PreviousImageClick(sender As Object, e As RoutedEventArgs)
            SelectRelativeImage(-1)
        End Sub

        Private Sub NextImageClick(sender As Object, e As RoutedEventArgs)
            SelectRelativeImage(1)
        End Sub

        Private Sub SelectRelativeImage(direction As Integer)
            Dim images = DocumentImages()
            Dim hasBackground = HasBackgroundImage()
            Dim total = images.Count + If(hasBackground, 1, 0)
            If total = 0 Then
                SetStatus("Noch kein Bild im Etikett vorhanden.", StatusKind.Warning)
                UpdateImageFocusControls()
                Return
            End If
            Dim currentIndex = If(_selectedImage Is Nothing, -1, images.IndexOf(_selectedImage))
            If _backgroundImageSelected AndAlso hasBackground Then currentIndex = images.Count
            Dim nextIndex = If(currentIndex < 0, If(direction > 0, 0, total - 1), (currentIndex + direction + total) Mod total)
            If nextIndex < images.Count Then
                SelectDocumentImage(images(nextIndex))
            Else
                SelectBackgroundImage()
            End If
        End Sub

        Private Sub ImageFocusToggleClick(sender As Object, e As RoutedEventArgs)
            If ImageFocusToggle.IsChecked.GetValueOrDefault() Then
                If _selectedImage Is Nothing AndAlso Not _backgroundImageSelected Then SelectRelativeImage(1)
            Else
                ClearImageFocus(True)
                SetStatus("Bildfokus ausgeschaltet – Text kann weiter bearbeitet werden.", StatusKind.Neutral)
            End If
        End Sub

        Private Function ReadImageStepDip() As Double
            Dim points As Double
            If Not TryParseDouble(ImageStepBox.Text, points) OrElse points < 0.1 OrElse points > 72 Then Throw New InvalidOperationException("Die Bild-Schrittweite muss zwischen 0,1 und 72 pt liegen.")
            Return LabelDocumentFactory.PointsToDip(points)
        End Function

        Private Function HasSelectedImageAsset() As Boolean
            If _selectedImage IsNot Nothing OrElse (_backgroundImageSelected AndAlso HasBackgroundImage()) Then Return True
            SetStatus("Zuerst ein Bild anklicken oder mit ‹ / › auswählen.", StatusKind.Warning)
            UpdateImageFocusControls()
            Return False
        End Function

        Private Sub MoveImageClick(sender As Object, e As RoutedEventArgs)
            If Not HasSelectedImageAsset() Then Return
            Try
                Dim stepDip = ReadImageStepDip()
                Dim direction = Convert.ToString(DirectCast(sender, FrameworkElement).Tag, CultureInfo.InvariantCulture)
                Dim dx = If(direction = "LEFT", -stepDip, If(direction = "RIGHT", stepDip, 0))
                Dim dy = If(direction = "UP", -stepDip, If(direction = "DOWN", stepDip, 0))

                If _backgroundImageSelected Then
                    Dim brush = DirectCast(LabelEditor.Document.Background, ImageBrush)
                    brush.Viewport = New Rect(brush.Viewport.X + dx, brush.Viewport.Y + dy, brush.Viewport.Width, brush.Viewport.Height)
                Else
                    Dim translate = TryCast(_selectedImage.RenderTransform, TranslateTransform)
                    If translate Is Nothing Then
                        translate = New TranslateTransform()
                        _selectedImage.RenderTransform = translate
                    End If
                    translate.X += dx
                    translate.Y += dy
                    _selectedImageAdorner?.InvalidateVisual()
                End If
                UpdateImageOffsetText()
                UpdateBackgroundSelectionOverlay()
                ScheduleFitCheck()
                SetStatus($"Bild um {ImageStepBox.Text.Trim()} pt nach {DirectionLabel(direction)} verschoben.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Bild nicht verschoben", ex.Message)
            End Try
        End Sub

        Private Shared Function DirectionLabel(direction As String) As String
            Select Case direction
                Case "LEFT" : Return "links"
                Case "RIGHT" : Return "rechts"
                Case "UP" : Return "oben"
                Case Else : Return "unten"
            End Select
        End Function

        Private Sub NudgeImageSizeClick(sender As Object, e As RoutedEventArgs)
            If Not HasSelectedImageAsset() Then Return
            Try
                Dim sign As Integer
                Integer.TryParse(Convert.ToString(DirectCast(sender, FrameworkElement).Tag, CultureInfo.InvariantCulture), sign)
                Dim deltaDip = ReadImageStepDip() * sign

                If _backgroundImageSelected Then
                    Dim brush = DirectCast(LabelEditor.Document.Background, ImageBrush)
                    Dim oldViewport = brush.Viewport
                    Dim nextWidth = Math.Clamp(oldViewport.Width + deltaDip, 2.0 / 25.4 * 96.0, 1000.0 / 25.4 * 96.0)
                    Dim ratio = oldViewport.Height / Math.Max(0.01, oldViewport.Width)
                    Dim nextHeight = nextWidth * ratio
                    Dim center = New Point(oldViewport.X + oldViewport.Width / 2.0, oldViewport.Y + oldViewport.Height / 2.0)
                    brush.Viewport = New Rect(center.X - nextWidth / 2.0, center.Y - nextHeight / 2.0, nextWidth, nextHeight)
                    ImageWidthBox.Text = (nextWidth / 96.0 * 25.4).ToString("0.#", CultureInfo.CurrentCulture)
                Else
                    Dim currentWidth = If(Double.IsNaN(_selectedImage.Width) OrElse _selectedImage.Width <= 0, _selectedImage.ActualWidth, _selectedImage.Width)
                    Dim nextWidth = Math.Clamp(currentWidth + deltaDip, 2.0 / 25.4 * 96.0, 1000.0 / 25.4 * 96.0)
                    _selectedImage.Width = nextWidth
                    _selectedImage.Height = Double.NaN
                    ImageWidthBox.Text = (nextWidth / 96.0 * 25.4).ToString("0.#", CultureInfo.CurrentCulture)
                    Dim blockContainer = TryCast(LogicalTreeHelper.GetParent(_selectedImage), BlockUIContainer)
                    Dim figure = If(blockContainer Is Nothing, Nothing, TryCast(LogicalTreeHelper.GetParent(blockContainer), Figure))
                    If figure IsNot Nothing Then figure.Width = New FigureLength(nextWidth)
                    _selectedImageAdorner?.InvalidateVisual()
                End If
                UpdateImageOffsetText()
                UpdateBackgroundSelectionOverlay()
                ScheduleFitCheck()
                SetStatus($"Bildbreite um {ImageStepBox.Text.Trim()} pt {If(sign < 0, "verkleinert", "vergrößert")}.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Bildgröße nicht geändert", ex.Message)
            End Try
        End Sub

        Private Sub ImageModeChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse LabelEditor Is Nothing OrElse ImageWidthBox Is Nothing OrElse ImageOpacityCombo Is Nothing Then Return
            If Not String.Equals(SelectedComboTag(ImageModeCombo, "INLINE"), "BACKGROUND", StringComparison.Ordinal) OrElse _selectedImage IsNot Nothing Then Return

            Dim brush = TryCast(LabelEditor.Document?.Background, ImageBrush)
            If brush Is Nothing OrElse brush.ImageSource Is Nothing OrElse brush.Viewport.Width <= 0 Then Return
            RemoveImageAdorner()
            _backgroundImageSelected = True
            ImageWidthBox.Text = (brush.Viewport.Width / 96.0 * 25.4).ToString("0.#", CultureInfo.CurrentCulture)
            SetImageOpacityControl(brush.Opacity)

            Dim profile = CurrentProfile()
            If profile IsNot Nothing Then
                Dim pageWidth = profile.WidthMm / 25.4 * 96.0
                If Math.Abs(brush.Viewport.X) < 0.5 Then
                    SelectComboByTag(ImageAlignmentCombo, "LEFT")
                ElseIf Math.Abs(brush.Viewport.Right - pageWidth) < 0.5 Then
                    SelectComboByTag(ImageAlignmentCombo, "RIGHT")
                Else
                    SelectComboByTag(ImageAlignmentCombo, "CENTER")
                End If
            End If
            UpdateImageFocusControls()
            SetStatus("Hintergrundgrafik geladen – Breite, Ausrichtung und Deckkraft können geändert werden.", StatusKind.Neutral)
        End Sub

        Private Sub SetImageOpacityControl(opacity As Double)
            Dim percent = CInt(Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 100))
            Dim matching = ImageOpacityCombo.Items.OfType(Of ComboBoxItem)().FirstOrDefault(
                Function(item) String.Equals(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), percent.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            If matching IsNot Nothing Then
                ImageOpacityCombo.SelectedItem = matching
            Else
                ImageOpacityCombo.SelectedItem = Nothing
                ImageOpacityCombo.Text = percent.ToString(CultureInfo.CurrentCulture) & " %"
            End If
        End Sub

        Private Shared Function ImageAlignmentTag(alignment As HorizontalAlignment) As String
            Select Case alignment
                Case HorizontalAlignment.Left
                    Return "LEFT"
                Case HorizontalAlignment.Right
                    Return "RIGHT"
                Case Else
                    Return "CENTER"
            End Select
        End Function

        Private Shared Function ImageModeTag(image As Image) As String
            Dim parent = LogicalTreeHelper.GetParent(image)
            If TypeOf parent Is BlockUIContainer Then
                Dim figure = TryCast(LogicalTreeHelper.GetParent(parent), Figure)
                If figure IsNot Nothing Then
                    Return If(figure.HorizontalAnchor = FigureHorizontalAnchor.ContentRight, "WRAP_RIGHT", "WRAP_LEFT")
                End If
                Return "BLOCK"
            End If
            Return "INLINE"
        End Function

        Private Sub ApplyImageSettingsClick(sender As Object, e As RoutedEventArgs)
            Try
                Dim mode = SelectedComboTag(ImageModeCombo, "INLINE")
                Dim backgroundBrush = TryCast(LabelEditor.Document.Background, ImageBrush)

                If mode = "BACKGROUND" Then
                    If _selectedImage IsNot Nothing Then
                        Dim source = TryCast(_selectedImage.Source, BitmapSource)
                        If source Is Nothing Then Throw New InvalidOperationException("Das markierte Bild kann nicht als Hintergrund verwendet werden.")
                        RemoveImageAdorner()
                        RemoveDocumentImage(_selectedImage)
                        _selectedImage = Nothing
                        ApplyBackgroundImage(source)
                        _backgroundImageSelected = True
                        UpdateImageFocusControls()
                        SetStatus("Markiertes Logo als Hintergrundgrafik übernommen.", StatusKind.Success)
                    Else
                        If backgroundBrush Is Nothing OrElse backgroundBrush.ImageSource Is Nothing Then Throw New InvalidOperationException("Bitte zuerst ein Logo im Etikett anklicken.")
                        ApplyBackgroundImage(DirectCast(backgroundBrush.ImageSource, BitmapSource), True)
                        _backgroundImageSelected = True
                        UpdateImageFocusControls()
                        SetStatus("Hintergrundgrafik aktualisiert.", StatusKind.Success)
                    End If
                Else
                    If _selectedImage Is Nothing AndAlso backgroundBrush IsNot Nothing AndAlso backgroundBrush.ImageSource IsNot Nothing Then
                        Dim source = TryCast(backgroundBrush.ImageSource, BitmapSource)
                        If source Is Nothing Then Throw New InvalidOperationException("Die Hintergrundgrafik kann nicht zurück in den Text verschoben werden.")
                        LabelEditor.Document.Background = Brushes.Transparent
                        _selectedImage = CreateDocumentImage(source)
                        InsertDocumentImage(_selectedImage, mode)
                        _backgroundImageSelected = False
                    End If
                    If _selectedImage Is Nothing Then Throw New InvalidOperationException("Bitte zuerst ein Logo im Etikett anklicken.")

                    _selectedImage.Width = ReadImageWidthDip()
                    ' Clipboard-Bilder besitzen häufig zusätzlich eine feste Höhe.
                    ' Nur die Breite zu ändern verkleinert dann zwar den Inhalt,
                    ' lässt aber den ursprünglichen großen Bildrahmen stehen.
                    _selectedImage.Height = Double.NaN
                    _selectedImage.MinWidth = 0
                    _selectedImage.MinHeight = 0
                    _selectedImage.Opacity = ReadImageOpacity()
                    _selectedImage.HorizontalAlignment = ReadImageHorizontalAlignment()

                    If Not String.Equals(ImageModeTag(_selectedImage), mode, StringComparison.Ordinal) Then
                        MoveDocumentImageToMode(_selectedImage, mode)
                    End If

                    Dim blockContainer = TryCast(LogicalTreeHelper.GetParent(_selectedImage), BlockUIContainer)
                    Dim figure = If(blockContainer Is Nothing, Nothing, TryCast(LogicalTreeHelper.GetParent(blockContainer), Figure))
                    If figure IsNot Nothing Then
                        figure.Width = New FigureLength(_selectedImage.Width)
                        Dim alignTag = SelectedComboTag(ImageAlignmentCombo, "CENTER")
                        figure.HorizontalAnchor = If(alignTag = "RIGHT", FigureHorizontalAnchor.ContentRight, FigureHorizontalAnchor.ContentLeft)
                        figure.WrapDirection = If(alignTag = "RIGHT", WrapDirection.Left, WrapDirection.Right)
                    End If
                    UpdateImageFocusControls()
                    AttachImageAdornerDeferred(_selectedImage)
                    SetStatus($"Logo-Einstellungen angewendet · Deckkraft {CInt(Math.Round(_selectedImage.Opacity * 100))} %.", StatusKind.Success)
                End If
                ScheduleFitCheck()
            Catch ex As Exception
                ShowInfo("Bildeinstellung nicht übernommen", ex.Message)
            End Try
        End Sub

        Private Sub MoveDocumentImageToMode(image As Image, mode As String)
            RemoveImageAdorner()
            RemoveDocumentImage(image)
            InsertDocumentImage(image, mode)
            _selectedImage = image
            _backgroundImageSelected = False
        End Sub

        Private Sub ApplyBackgroundImage(source As BitmapSource, Optional preservePosition As Boolean = False)
            Dim profile = CurrentProfile()
            If profile Is Nothing Then Throw New InvalidOperationException("Kein Etikettenformat gewählt.")
            Dim pageWidth = profile.WidthMm / 25.4 * 96.0
            Dim pageHeight = profile.HeightMm / 25.4 * 96.0
            Dim width = ReadImageWidthDip()
            Dim height = width * source.PixelHeight / Math.Max(1.0, source.PixelWidth)
            Dim existingBrush = TryCast(LabelEditor.Document.Background, ImageBrush)
            Dim x As Double
            Dim y As Double
            If preservePosition AndAlso existingBrush IsNot Nothing AndAlso existingBrush.Viewport.Width > 0 Then
                Dim oldCenter = New Point(existingBrush.Viewport.X + existingBrush.Viewport.Width / 2.0, existingBrush.Viewport.Y + existingBrush.Viewport.Height / 2.0)
                x = oldCenter.X - width / 2.0
                y = oldCenter.Y - height / 2.0
            Else
                Dim alignTag = SelectedComboTag(ImageAlignmentCombo, "CENTER")
                x = If(alignTag = "LEFT", 0, If(alignTag = "RIGHT", pageWidth - width, (pageWidth - width) / 2))
                y = (pageHeight - height) / 2
            End If
            LabelEditor.Document.Background = New ImageBrush(source) With {
                .Opacity = ReadImageOpacity(),
                .Stretch = Stretch.Fill,
                .TileMode = TileMode.None,
                .ViewportUnits = BrushMappingMode.Absolute,
                .Viewport = New Rect(x, y, width, height)
            }
        End Sub

        Private Sub RemoveImageClick(sender As Object, e As RoutedEventArgs)
            If _backgroundImageSelected OrElse (_selectedImage Is Nothing AndAlso TypeOf LabelEditor.Document.Background Is ImageBrush) Then
                LabelEditor.Document.Background = Brushes.Transparent
                _backgroundImageSelected = False
                UpdateImageFocusControls()
                SetStatus("Hintergrundgrafik entfernt.", StatusKind.Success)
                ScheduleFitCheck()
                Return
            End If
            If _selectedImage Is Nothing Then
                ShowInfo("Kein Logo ausgewählt", "Bitte das Logo im Etikett anklicken oder ‚Hintergrundgrafik‘ auswählen.")
                Return
            End If
            RemoveImageAdorner()
            RemoveDocumentImage(_selectedImage)
            _selectedImage = Nothing
            UpdateImageFocusControls()
            ScheduleFitCheck()
            SetStatus("Logo entfernt.", StatusKind.Success)
        End Sub

        Private Shared Sub RemoveDocumentImage(image As Image)
            Dim parent = LogicalTreeHelper.GetParent(image)
            If TypeOf parent Is InlineUIContainer Then
                Dim container = DirectCast(parent, InlineUIContainer)
                Dim paragraph = TryCast(LogicalTreeHelper.GetParent(container), Paragraph)
                container.Child = Nothing
                paragraph?.Inlines.Remove(container)
            ElseIf TypeOf parent Is BlockUIContainer Then
                Dim container = DirectCast(parent, BlockUIContainer)
                Dim contentParent = LogicalTreeHelper.GetParent(container)
                container.Child = Nothing
                If TypeOf contentParent Is Figure Then
                    Dim figure = DirectCast(contentParent, Figure)
                    Dim paragraph = TryCast(LogicalTreeHelper.GetParent(figure), Paragraph)
                    paragraph?.Inlines.Remove(figure)
                ElseIf TypeOf contentParent Is FlowDocument Then
                    DirectCast(contentParent, FlowDocument).Blocks.Remove(container)
                End If
            End If
        End Sub

        Private Function ReadImageWidthDip() As Double
            Dim widthMm As Double
            If Not TryParseDouble(ImageWidthBox.Text, widthMm) OrElse widthMm < 2 OrElse widthMm > 1000 Then Throw New InvalidOperationException("Die Bildbreite muss zwischen 2 und 1000 mm liegen.")
            Return widthMm / 25.4 * 96.0
        End Function

        Private Function ReadImageOpacity() As Double
            Dim textValue = If(ImageOpacityCombo.Text, "").Replace("%", "").Trim()
            Dim opacityPercent As Double
            If Not TryParseDouble(textValue, opacityPercent) Then
                Double.TryParse(SelectedComboTag(ImageOpacityCombo, "100"), NumberStyles.Float, CultureInfo.InvariantCulture, opacityPercent)
            End If
            If opacityPercent < 5 OrElse opacityPercent > 100 Then Throw New InvalidOperationException("Die Deckkraft muss zwischen 5 und 100 Prozent liegen.")
            Return opacityPercent / 100.0
        End Function

        Private Function ReadImageHorizontalAlignment() As HorizontalAlignment
            Select Case SelectedComboTag(ImageAlignmentCombo, "CENTER")
                Case "LEFT"
                    Return HorizontalAlignment.Left
                Case "RIGHT"
                    Return HorizontalAlignment.Right
                Case Else
                    Return HorizontalAlignment.Center
            End Select
        End Function

        Private Shared Function SelectedComboTag(combo As ComboBox, fallback As String) As String
            Dim item = TryCast(combo.SelectedItem, ComboBoxItem)
            Dim value = Convert.ToString(item?.Tag, CultureInfo.InvariantCulture)
            Return If(String.IsNullOrWhiteSpace(value), fallback, value)
        End Function

        Private Sub LineHeightChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse _isSynchronizingFormatControls OrElse LabelEditor Is Nothing Then Return
            If LineHeightCombo.IsDropDownOpen Then Return
            Dim selectedText = If(e.AddedItems.Count > 0, Convert.ToString(e.AddedItems(0), CultureInfo.CurrentCulture), LineHeightCombo.Text)
            ApplyLineHeightFromControl(selectedText)
        End Sub

        Private Sub LineHeightLostFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
            ApplyLineHeightFromControl()
        End Sub

        Private Sub LineHeightDropDownClosed(sender As Object, e As EventArgs)
            If _isInitializing OrElse _isSynchronizingFormatControls Then Return
            ApplyLineHeightFromControl()
        End Sub

        Private Sub ApplyLineHeightFromControl(Optional requestedText As String = Nothing)
            Dim textValue = If(requestedText, LineHeightCombo.Text).Trim()
            If String.Equals(textValue, "Auto", StringComparison.OrdinalIgnoreCase) Then
                For Each paragraph In GetSelectedParagraphs()
                    paragraph.LineHeight = Double.NaN
                    paragraph.LineStackingStrategy = LineStackingStrategy.MaxHeight
                Next
                LabelEditor.Focus()
                ScheduleFitCheck()
                SetStatus("Automatische Zeilenhöhe angewendet.", StatusKind.Success)
                Return
            End If
            Dim points As Double
            If Not TryParseDouble(textValue, points) OrElse points < 4 OrElse points > 100 Then Return
            For Each paragraph In GetSelectedParagraphs()
                paragraph.LineHeight = LabelDocumentFactory.PointsToDip(points)
                paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            Next
            LabelEditor.Focus()
            ScheduleFitCheck()
            SetStatus($"Zeilenhöhe {points:0.#} pt angewendet.", StatusKind.Success)
        End Sub

        Private Sub LineHeightKeyDown(sender As Object, e As KeyEventArgs)
            If e.Key <> Key.Enter Then Return
            ApplyLineHeightFromControl()
            e.Handled = True
        End Sub

        Private Sub DecreaseLineHeightClick(sender As Object, e As RoutedEventArgs)
            AdjustLineHeight(-0.5)
        End Sub

        Private Sub IncreaseLineHeightClick(sender As Object, e As RoutedEventArgs)
            AdjustLineHeight(0.5)
        End Sub

        Private Sub AdjustLineHeight(delta As Double)
            Dim paragraphs = GetSelectedParagraphs()
            If paragraphs.Count = 0 Then Return
            Dim paragraph = paragraphs(0)
            Dim currentDip = paragraph.LineHeight
            If Double.IsNaN(currentDip) OrElse currentDip <= 0 Then currentDip = paragraph.FontSize * 1.08
            Dim points = Math.Clamp(LabelDocumentFactory.DipToPoints(currentDip) + delta, 4, 100)
            LineHeightCombo.Text = points.ToString("0.#", CultureInfo.CurrentCulture)
            ApplyLineHeightFromControl(LineHeightCombo.Text)
        End Sub

        Private Sub QuickMarginChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse _isSynchronizingFormatControls Then Return
            Dim selectedText = If(e.AddedItems.Count > 0, Convert.ToString(e.AddedItems(0), CultureInfo.CurrentCulture), QuickMarginCombo.Text)
            ApplyQuickMargin(selectedText)
        End Sub

        Private Sub QuickMarginLostFocus(sender As Object, e As KeyboardFocusChangedEventArgs)
            ApplyQuickMargin()
        End Sub

        Private Sub QuickMarginKeyDown(sender As Object, e As KeyEventArgs)
            If e.Key <> Key.Enter Then Return
            ApplyQuickMargin()
            e.Handled = True
        End Sub

        Private Sub DecreaseMarginClick(sender As Object, e As RoutedEventArgs)
            AdjustQuickMargin(-0.5)
        End Sub

        Private Sub IncreaseMarginClick(sender As Object, e As RoutedEventArgs)
            AdjustQuickMargin(0.5)
        End Sub

        Private Sub AdjustQuickMargin(delta As Double)
            Dim printer = CurrentPrinter()
            If printer Is Nothing Then Return
            Dim margin = Math.Clamp(printer.MarginMm + delta, 0, 8)
            QuickMarginCombo.Text = margin.ToString("0.##", CultureInfo.CurrentCulture)
            ApplyQuickMargin(QuickMarginCombo.Text)
        End Sub

        Private Sub ApplyQuickMargin(Optional requestedText As String = Nothing)
            If _isInitializing OrElse _isSynchronizingFormatControls Then Return
            Dim margin As Double
            Dim textValue = If(requestedText, QuickMarginCombo.Text)
            If Not TryParseDouble(textValue, margin) OrElse margin < 0 OrElse margin > 8 Then
                SetStatus("Innenrand muss zwischen 0 und 8 mm liegen.", StatusKind.Warning)
                Return
            End If
            Dim printer = CurrentPrinter()
            If printer Is Nothing Then Return
            printer.MarginMm = margin
            SyncLegacyPrinterFields(printer)
            SyncPrintersToSettings()
            SettingsStore.Save(_settings)
            If ReferenceEquals(SettingsPrinterList.SelectedItem, printer) Then MarginBox.Text = margin.ToString("0.##", CultureInfo.CurrentCulture)
            UpdateLabelDimensions()
            ScheduleFitCheck()
            SetStatus($"Innenrand {margin:0.##} mm – nutzbare Etikettenfläche sofort aktualisiert.", StatusKind.Success)
            LabelEditor.Focus()
        End Sub

        Private Sub UpdateQuickMarginControl(printer As PrinterProfile)
            If printer Is Nothing Then Return
            _isSynchronizingFormatControls = True
            Try
                QuickMarginCombo.Text = printer.MarginMm.ToString("0.##", CultureInfo.CurrentCulture)
            Finally
                _isSynchronizingFormatControls = False
            End Try
        End Sub

        Private Function GetSelectedParagraphs() As IReadOnlyList(Of Paragraph)
            Dim startPosition = LabelEditor.Selection.Start
            Dim endPosition = LabelEditor.Selection.End
            Dim paragraphs = LabelEditor.Document.Blocks.OfType(Of Paragraph)().
                Where(Function(paragraph) paragraph.ContentEnd.CompareTo(startPosition) >= 0 AndAlso
                                              paragraph.ContentStart.CompareTo(endPosition) <= 0).
                ToList()
            If paragraphs.Count = 0 AndAlso startPosition.Paragraph IsNot Nothing Then paragraphs.Add(startPosition.Paragraph)
            Return paragraphs
        End Function

        Private Sub AlignLeftClick(sender As Object, e As RoutedEventArgs)
            EditingCommands.AlignLeft.Execute(Nothing, LabelEditor)
            LabelEditor.Focus()
        End Sub

        Private Sub AlignCenterClick(sender As Object, e As RoutedEventArgs)
            EditingCommands.AlignCenter.Execute(Nothing, LabelEditor)
            LabelEditor.Focus()
        End Sub

        Private Sub AlignRightClick(sender As Object, e As RoutedEventArgs)
            EditingCommands.AlignRight.Execute(Nothing, LabelEditor)
            LabelEditor.Focus()
        End Sub

        Private Sub UndoClick(sender As Object, e As RoutedEventArgs)
            If LabelEditor.CanUndo Then LabelEditor.Undo()
            LabelEditor.Focus()
        End Sub

        Private Sub NewLabelClick(sender As Object, e As RoutedEventArgs)
            If Not Confirm("Freies Etikett beginnen?", "Der aktuelle Text wird aus dem Editor entfernt.", "Nicht gespeicherter Etikettentext wird verworfen.", "Neu beginnen") Then Return
            SetLabelDocument(LabelDocumentFactory.CreateBaseDocument())
            _settings.LastUsedTemplateId = ""
            SettingsStore.Save(_settings)
            LabelEditor.Focus()
            SetStatus("Neues freies Etikett bereit.", StatusKind.Success)
        End Sub

        Private Sub EditorSelectionChanged(sender As Object, e As RoutedEventArgs)
            If _isInitializing OrElse _isSynchronizingFormatControls Then Return
            _isSynchronizingFormatControls = True
            Try
                Dim familyValue = LabelEditor.Selection.GetPropertyValue(TextElement.FontFamilyProperty)
                If familyValue IsNot DependencyProperty.UnsetValue Then
                    Dim family = TryCast(familyValue, FontFamily)
                    If family IsNot Nothing Then FontFamilyCombo.Text = family.Source
                End If
                Dim sizeValue = LabelEditor.Selection.GetPropertyValue(TextElement.FontSizeProperty)
                If sizeValue IsNot DependencyProperty.UnsetValue AndAlso TypeOf sizeValue Is Double Then
                    FontSizeCombo.Text = LabelDocumentFactory.DipToPoints(CDbl(sizeValue)).ToString("0.#", CultureInfo.CurrentCulture)
                End If
                Dim lineHeightValue = LabelEditor.Selection.GetPropertyValue(Block.LineHeightProperty)
                If lineHeightValue IsNot DependencyProperty.UnsetValue AndAlso TypeOf lineHeightValue Is Double AndAlso Not Double.IsNaN(CDbl(lineHeightValue)) Then
                    LineHeightCombo.Text = LabelDocumentFactory.DipToPoints(CDbl(lineHeightValue)).ToString("0.#", CultureInfo.CurrentCulture)
                Else
                    LineHeightCombo.Text = "Auto"
                End If
            Finally
                _isSynchronizingFormatControls = False
            End Try
        End Sub

        Private Sub EditorTextChanged(sender As Object, e As TextChangedEventArgs)
            If Not _isInitializing Then
                ScheduleFitCheck()
                UpdateImageFocusControls()
            End If
        End Sub

        Private Sub ScheduleFitCheck()
            _fitTimer.Stop()
            _fitTimer.Start()
        End Sub

        Private Sub FormattingMarksChanged(sender As Object, e As RoutedEventArgs)
            If FormattingMarks Is Nothing Then Return
            FormattingMarks.Visibility = If(FormattingMarksToggle.IsChecked.GetValueOrDefault(), Visibility.Visible, Visibility.Collapsed)
            FormattingMarks.InvalidateVisual()
        End Sub

        Private Sub FitTimerTick(sender As Object, e As EventArgs)
            _fitTimer.Stop()
            Try
                Dim profile = CurrentProfile()
                Dim printer = CurrentPrinter()
                If profile Is Nothing OrElse printer Is Nothing Then Return
                Dim pages = LabelRenderer.GetPageCount(LabelEditor.Document, profile, printer.MarginMm)
                _contentFits = pages <= 1
                Dim usableWidth = Math.Max(0, profile.WidthMm - 2 * printer.MarginMm)
                Dim usableHeight = Math.Max(0, profile.HeightMm - 2 * printer.MarginMm)
                If _contentFits Then
                    ContentFitText.Text = $"✓ Inhalt passt · Nutzfläche {usableWidth:0.##} × {usableHeight:0.##} mm · Rand {printer.MarginMm:0.##} mm"
                    ContentFitText.Foreground = New SolidColorBrush(Color.FromRgb(19, 122, 82))
                    LabelOutline.BorderBrush = New SolidColorBrush(Color.FromRgb(72, 159, 217))
                Else
                    ContentFitText.Text = $"Möglicher Überlauf (Layoutprüfung: {pages} Seiten). Drucken ist möglich; außerhalb der Etikettenfläche wird abgeschnitten. Absatzabstände, Zeilenhöhe und Rand prüfen."
                    ContentFitText.ToolTip = "Mit ¶ lassen sich Leerzeichen, Tabulatoren und Absatzenden anzeigen. Die Seitenprüfung kann vom sichtbaren Ausschnitt abweichen."
                    ContentFitText.Foreground = New SolidColorBrush(Color.FromRgb(182, 106, 19))
                    LabelOutline.BorderBrush = New SolidColorBrush(Color.FromRgb(214, 121, 26))
                End If
                UpdatePrintButtonState()
            Catch
                _contentFits = False
                ContentFitText.Text = "Layoutprüfung nicht verfügbar. Drucken ist möglich; außerhalb der Etikettenfläche wird abgeschnitten."
                UpdatePrintButtonState()
            End Try
        End Sub

        Private Sub UpdatePrintButtonState()
            If _isPrinting Then
                PrintButton.IsEnabled = False
                PrintButton.Content = "Druck läuft..."
            ElseIf _contentFits Then
                PrintButton.IsEnabled = True
                PrintButton.Content = "Etikett drucken"
            Else
                PrintButton.IsEnabled = True
                PrintButton.Content = "Trotz Überlauf drucken"
            End If
        End Sub

        Private Sub RefreshNewestLaborFile()
            _newestLaborPath = LaborauftragParser.FindNewest(DownloadDirectoryBox.Text)
            If _newestLaborPath = "" Then
                NewestLaborText.Text = "Keine Laborauftrag*.txt im eingestellten Ordner erkannt."
            Else
                Dim info As New FileInfo(_newestLaborPath)
                NewestLaborText.Text = info.Name & Environment.NewLine & info.LastWriteTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)
            End If
        End Sub

        Private Sub LoadNewestLaborClick(sender As Object, e As RoutedEventArgs)
            RefreshNewestLaborFile()
            If _newestLaborPath = "" Then
                ShowInfo("Keine Laborauftrag-Datei", "Im eingestellten Download-Ordner wurde keine passende Datei gefunden.", DownloadDirectoryBox.Text, True)
                Return
            End If
            LoadLaborFile(_newestLaborPath)
        End Sub

        Private Async Sub ClipboardOcrClick(sender As Object, e As RoutedEventArgs)
            Try
                If Not Clipboard.ContainsImage() Then
                    ShowInfo("Kein Screencut in der Zwischenablage", "Bitte in Offisy zuerst den Patienten-Screencut erstellen oder einen Bildschirmbereich kopieren.", "Danach diesen Button erneut anklicken.", True)
                    Return
                End If
                Dim clipboardImage As BitmapSource = Clipboard.GetImage()
                If clipboardImage Is Nothing Then Throw New InvalidDataException("Das Zwischenablage-Bild konnte nicht gelesen werden.")

                ClipboardOcrButton.IsEnabled = False
                ClipboardOcrButton.Content = "OCR liest Patientendaten..."
                SetStatus("Offisy-Screencut wird lokal gelesen; es wird nichts gespeichert.", StatusKind.Busy)
                Dim recognizedText = Await ClipboardOcrService.RecognizeAsync(clipboardImage)
                Dim result = OffisyOcrParser.Parse(recognizedText)
                Dim appliedTemplate = ApplyPatientDataToCurrentTemplate(result.Data, result.DoctorName)
                RootTabs.SelectedIndex = 0

                If result.Warnings.Count = 0 Then
                    SetStatus($"Clipboard-Screencut erkannt und {appliedTemplate} eingesetzt – bitte vor dem Druck kurz prüfen.", StatusKind.Success)
                Else
                    SetStatus($"OCR-Vorlage eingesetzt – {result.Warnings.Count} Angabe(n) bitte prüfen.", StatusKind.Warning)
                    ShowInfo("OCR-Vorlage bitte prüfen", "Die erkannten Angaben wurden in den Editor eingesetzt. OCR kann Zeichen verwechseln; bitte insbesondere Name und Versicherungsnummer kontrollieren.", String.Join(Environment.NewLine, result.Warnings), True)
                End If
            Catch ex As Exception
                ShowInfo("Clipboard-OCR fehlgeschlagen", ex.Message, "Das Bild und der OCR-Rohtext wurden nicht gespeichert.", True)
                SetStatus("Clipboard-OCR fehlgeschlagen; Laborauftrag-Import bleibt verfügbar.", StatusKind.ErrorState)
            Finally
                ClipboardOcrButton.IsEnabled = True
                ClipboardOcrButton.Content = "Clipboard-Screencut per OCR übernehmen"
                EnsureEditorEditable()
                LabelEditor.Focus()
            End Try
        End Sub

        Private Sub ChooseLaborFileClick(sender As Object, e As RoutedEventArgs)
            Dim dialog As New OpenFileDialog With {
                .Title = "Laborauftrag auswählen",
                .Filter = "Laborauftrag / Textdatei (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
                .InitialDirectory = If(Directory.Exists(DownloadDirectoryBox.Text), DownloadDirectoryBox.Text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            }
            If dialog.ShowDialog(Me) = True Then LoadLaborFile(dialog.FileName)
        End Sub

        Private Sub BrainOfficePasteClick(sender As Object, e As RoutedEventArgs)
            ' Ein bewusster Klick muss dieselben Patientendaten erneut in eine danach
            ' geladene Vorlage einsetzen dürfen. Nur der automatische Import wird dedupliziert.
            TryImportBrainOfficeClipboard(True, True)
        End Sub

        Private Function TryImportBrainOfficeClipboard(showErrors As Boolean, Optional forceReapply As Boolean = False) As Boolean
            If _isTemplateDesignMode Then
                If showErrors Then SetStatus("Vorlagenmodus schützt den Entwurf · BrainOffice-Import ist pausiert.", StatusKind.Warning)
                Return False
            End If
            If _isImportingBrainOfficeClipboard Then Return False
            _isImportingBrainOfficeClipboard = True
            Try
                Dim transfer As BrainOfficeClipboardTransfer = Nothing
                Dim parseError = "Die Zwischenablage enthält keine passenden BrainOffice-Patientendaten."
                If Clipboard.ContainsText(TextDataFormat.UnicodeText) OrElse Clipboard.ContainsText(TextDataFormat.Text) Then
                    Dim json = Clipboard.GetText(TextDataFormat.UnicodeText)
                    If String.IsNullOrWhiteSpace(json) Then json = Clipboard.GetText()
                    If json.Contains(BrainOfficeClipboardTransfer.SupportedSchema, StringComparison.Ordinal) Then
                        If BrainOfficeClipboardImport.TryParse(json, transfer, parseError) Then
                            _lastBrainOfficeTransfer = transfer
                        End If
                    End If
                End If

                ' Ein bewusster Paste-Klick darf die zuletzt erfolgreich gelesene Übergabe
                ' erneut verwenden, falls ein anderes Programm das Windows-Clipboard inzwischen
                ' verändert hat. Die Daten bleiben nur im Arbeitsspeicher dieser Sitzung.
                If transfer Is Nothing AndAlso forceReapply Then transfer = _lastBrainOfficeTransfer
                If transfer Is Nothing Then
                    If showErrors Then ShowInfo("BrainOffice-Daten fehlen", parseError, "BrainOffice erneut aufrufen oder Patient dort nochmals an das Etikettenstudio übergeben.", True)
                    Return False
                End If
                If Not forceReapply AndAlso _processedBrainOfficeClipboardKeys.Contains(transfer.DedupeKey) Then
                    If showErrors Then SetStatus("Diese BrainOffice-Übergabe wurde bereits übernommen.", StatusKind.Neutral)
                    Return False
                End If

                If Not String.IsNullOrWhiteSpace(transfer.Profile) Then
                    Dim requested = _profiles.FirstOrDefault(Function(item) String.Equals(item.Id, transfer.Profile, StringComparison.OrdinalIgnoreCase) OrElse String.Equals(item.Name, transfer.Profile, StringComparison.CurrentCultureIgnoreCase))
                    If requested IsNot Nothing Then ProfileCombo.SelectedItem = requested
                End If
                If CopiesCombo.Items.Count > 0 Then CopiesCombo.SelectedIndex = Math.Clamp(transfer.Copies - 1, 0, CopiesCombo.Items.Count - 1)
                Dim appliedTemplate = ApplyPatientDataToCurrentTemplate(transfer.Patient)
                _processedBrainOfficeClipboardKeys.Add(transfer.DedupeKey)
                RootTabs.SelectedIndex = 0
                ScheduleFitCheck()
                SetStatus($"BrainOffice-Patientendaten übernommen · {appliedTemplate} · Schema v1 · bitte prüfen und drucken.", StatusKind.Success)
                Return True
            Catch ex As Exception
                If showErrors Then ShowInfo("BrainOffice-Daten konnten nicht übernommen werden", ex.Message, "Die Zwischenablage wurde nicht verändert.", True)
                Return False
            Finally
                _isImportingBrainOfficeClipboard = False
            End Try
        End Function

        Private Sub LoadLaborFile(pathValue As String)
            Try
                Dim result = LaborauftragParser.Parse(pathValue)
                Dim appliedTemplate = ApplyPatientDataToCurrentTemplate(result.Data)
                RootTabs.SelectedIndex = 0
                If result.Warnings.Count = 0 Then
                    SetStatus($"Laborauftrag eingelesen und {appliedTemplate} eingesetzt.", StatusKind.Success)
                Else
                    SetStatus($"Laborauftrag eingelesen – {result.Warnings.Count} Angabe(n) fehlen.", StatusKind.Warning)
                    ShowInfo("Patientenvorlage mit Hinweisen", "Die vorhandenen Angaben wurden eingesetzt. Bitte fehlende Daten vor dem Druck prüfen.", String.Join(Environment.NewLine, result.Warnings), True)
                End If
            Catch ex As Exception
                ShowInfo("Laborauftrag konnte nicht gelesen werden", ex.Message, Path.GetFileName(pathValue), True)
                SetStatus("Laborauftrag-Import fehlgeschlagen.", StatusKind.ErrorState)
            End Try
        End Sub

        Private Function ApplyPatientDataToCurrentTemplate(patientData As PatientLabelData,
                                                           Optional doctorName As String = "") As String
            If patientData Is Nothing Then
                Throw New ArgumentNullException(NameOf(patientData))
            End If

            Dim storedTemplate = _activeTemplate
            Dim document As FlowDocument = Nothing
            Dim description = "Standard-Patientenvorlage"

            If storedTemplate IsNot Nothing Then
                Dim restored = LabelTemplateStore.RestoreDocument(storedTemplate)
                If LabelTemplateEngine.ContainsPlaceholders(restored) Then
                    document = restored
                    description = "Vorlage " & storedTemplate.Name
                End If
            End If

            If document Is Nothing AndAlso LabelTemplateEngine.ContainsPlaceholders(LabelEditor.Document) Then
                document = LabelRenderer.CloneDocument(LabelEditor.Document)
                description = "aktuelle Platzhalter-Vorlage"
            End If

            If document Is Nothing Then
                Dim footer = LabelDocumentFactory.BuildPracticeFooter(doctorName, PracticeLineBox.Text)
                SetLabelDocument(LabelDocumentFactory.CreatePatientTemplate(patientData, footer))
                Return description
            End If

            Dim replacements = LabelTemplateEngine.ApplyPatientData(document, patientData, doctorName, PracticeLineBox.Text)
            If replacements = 0 Then
                Throw New InvalidDataException("Die gewählte Vorlage enthält keine erkennbaren Patienten-Platzhalter.")
            End If
            SetLabelDocument(document)
            _activeTemplate = storedTemplate
            Return description
        End Function

        Private Function ApplyPatientDataToStoredTemplate(patientData As PatientLabelData) As String
            If patientData Is Nothing Then Throw New ArgumentNullException(NameOf(patientData))

            Dim storedTemplate = _activeTemplate
            If storedTemplate Is Nothing Then
                Throw New InvalidDataException("Bitte zuerst eine passende lokale Etikettenvorlage auswählen und speichern.")
            End If

            Dim document = LabelTemplateStore.RestoreDocument(storedTemplate)
            If Not LabelTemplateEngine.ContainsPlaceholders(document) OrElse
               LabelTemplateEngine.ApplyPatientData(document, patientData, "", PracticeLineBox.Text) = 0 Then
                Throw New InvalidDataException("Die zuletzt verwendete Etikettenvorlage enthält keine Patienten-Platzhalter.")
            End If

            SetLabelDocument(document)
            _activeTemplate = storedTemplate
            Return storedTemplate.Name
        End Function

        Private Async Sub ConnectionTestClick(sender As Object, e As RoutedEventArgs)
            Dim testPrinter As PrinterProfile = Nothing
            Try
                Dim selected = TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)
                testPrinter = ReadPrinterEditor(If(selected?.Id, ""))
                ConnectionTestButton.IsEnabled = False
                ConnectionTestButton.Content = "Verbindung wird geprüft..."
                Dim testHint = If(testPrinter.UsesSupvanUsb, "synthetischer Test ohne Patientendaten", "es wird nichts gedruckt")
                PrinterManagerStatusText.Text = $"Teste {testPrinter.Name} · {testPrinter.ConnectionText} – {testHint}..."
                SetStatus($"Prüfe {testPrinter.Name} · {testPrinter.ConnectionText} – {testHint}.", StatusKind.Busy)
                Using timeout As New CancellationTokenSource(TimeSpan.FromSeconds(4))
                    Await RawLabelPrinter.TestConnectionAsync(testPrinter, timeout.Token)
                End Using
                If CurrentPrinter() IsNot Nothing AndAlso String.Equals(CurrentPrinter().Id, testPrinter.Id, StringComparison.OrdinalIgnoreCase) Then ConnectionDot.Fill = New SolidColorBrush(Color.FromRgb(143, 226, 174))
                PrinterManagerStatusText.Text = $"Erreichbar: {testPrinter.Name} · {testPrinter.ConnectionText}. Keine Patientendaten gesendet."
                SetStatus($"Drucker {testPrinter.Name} erreichbar. Keine Patientendaten gesendet.", StatusKind.Success)
            Catch ex As OperationCanceledException
                If IsCurrentTarget(testPrinter) Then ConnectionDot.Fill = New SolidColorBrush(Color.FromRgb(235, 168, 96))
                PrinterManagerStatusText.Text = "Verbindungstest hat das Zeitlimit überschritten."
                SetStatus("Verbindungstest hat das Zeitlimit überschritten.", StatusKind.Warning)
            Catch ex As Exception
                If IsCurrentTarget(testPrinter) Then ConnectionDot.Fill = New SolidColorBrush(Color.FromRgb(224, 108, 108))
                ShowInfo("Drucker nicht erreichbar", "Die Verbindung, Windows-Warteschlange oder SUPVAN-Geräteauswahl konnte nicht geöffnet werden.", ex.Message, True)
                PrinterManagerStatusText.Text = "Verbindung fehlgeschlagen: " & ex.Message
                SetStatus("Verbindungstest fehlgeschlagen.", StatusKind.ErrorState)
            Finally
                ConnectionTestButton.IsEnabled = True
                ConnectionTestButton.Content = "Verbindung testen (druckt nicht)"
                UpdatePrinterSummary()
            End Try
        End Sub

        Private Function IsCurrentTarget(printer As PrinterProfile) As Boolean
            Dim current = CurrentPrinter()
            Return printer IsNot Nothing AndAlso current IsNot Nothing AndAlso
                String.Equals(current.Id, printer.Id, StringComparison.OrdinalIgnoreCase)
        End Function

        Private Async Sub PrintClick(sender As Object, e As RoutedEventArgs)
            Dim profile = CurrentProfile()
            Dim printer = CurrentPrinter()
            If profile Is Nothing OrElse printer Is Nothing Then Return
            If String.IsNullOrWhiteSpace(LabelDocumentFactory.GetPlainText(LabelEditor.Document)) Then
                ShowInfo("Etikett ist leer", "Bitte zuerst Text eingeben oder einen Laborauftrag übernehmen.")
                Return
            End If

            Try
                ReadPrinterSettingsFromControls()
                Dim copies = GetCopies()
                SettingsStore.Save(_settings)
                _isPrinting = True
                UpdatePrintButtonState()
                SetStatus($"Sende {copies} Etikett(en) an {printer.Name} · {printer.ConnectionText}...", StatusKind.Busy)
                Using timeout As New CancellationTokenSource(TimeSpan.FromSeconds(18))
                    Await RawLabelPrinter.PrintAsync(LabelEditor.Document, profile, _settings, printer, copies, timeout.Token)
                End Using
                ConnectionDot.Fill = New SolidColorBrush(Color.FromRgb(143, 226, 174))
                Dim historyNote = ""
                If SaveHistoryCheck.IsChecked.GetValueOrDefault() Then
                    Try
                        PrintHistoryStore.Add(LabelEditor.Document, profile, copies, _settings.NormalPrint, printer.Name, printer.Language)
                        RefreshHistory()
                        historyNote = " Im lokalen Verlauf gespeichert."
                    Catch historyError As Exception
                        historyNote = " Verlauf konnte nicht gespeichert werden."
                        ShowInfo("Etikett gedruckt – Verlauf nicht gespeichert", "Der Druckauftrag war erfolgreich, die portable Verlaufsdatei konnte aber nicht geschrieben werden.", historyError.Message, True)
                    End Try
                End If
                SetStatus($"{copies} Etikett(en) erfolgreich an {printer.Name} übertragen · {printer.Language} · {profile.WidthMm:0.##} × {profile.HeightMm:0.##} mm.{historyNote}", StatusKind.Success)
            Catch ex As OperationCanceledException
                ShowInfo("Druckauftrag abgebrochen", "Der Drucker hat innerhalb des Zeitlimits nicht geantwortet.", "Es ist unklar, ob der Auftrag vollständig empfangen wurde.", True)
                SetStatus("Druckauftrag mit Zeitüberschreitung beendet.", StatusKind.Warning)
            Catch ex As Exception
                ShowInfo("Drucken fehlgeschlagen", "Der Druckauftrag konnte nicht vollständig an den gewählten Druckweg übertragen werden.", ex.Message, True)
                SetStatus("Druckauftrag fehlgeschlagen.", StatusKind.ErrorState)
            Finally
                _isPrinting = False
                UpdatePrintButtonState()
            End Try
        End Sub

        Private Function GetCopies() As Integer
            Dim item = TryCast(CopiesCombo.SelectedItem, ComboBoxItem)
            Dim result As Integer
            If item IsNot Nothing AndAlso Integer.TryParse(CStr(item.Tag), result) Then Return result
            Return 1
        End Function

        Private Sub SaveSettingsClick(sender As Object, e As RoutedEventArgs)
            Try
                ReadPrinterSettingsFromControls()
                SettingsStore.Save(_settings)
                RefreshNewestLaborFile()
                UpdateLabelDimensions()
                UpdateSharedTemplateStatus()
                SetStatus("Einstellungen lokal gespeichert.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Einstellungen nicht gespeichert", ex.Message, "Bitte Eingaben prüfen.", True)
            End Try
        End Sub

        Private Sub ReadPrinterSettingsFromControls()
            _settings.NormalPrint = Not InversePrintCheck.IsChecked.GetValueOrDefault()
            _settings.HistoryEnabled = SaveHistoryCheck.IsChecked.GetValueOrDefault()
            _settings.DownloadDirectory = DownloadDirectoryBox.Text.Trim()
            _settings.PracticeLine = If(String.IsNullOrWhiteSpace(PracticeLineBox.Text), LabelDocumentFactory.OfficeLine, PracticeLineBox.Text.Trim())
            If SharedTemplatePathBox IsNot Nothing Then _settings.SharedTemplatePath = SharedTemplatePathBox.Text.Trim()
            Dim targetItem = TryCast(TemplateSaveTargetCombo?.SelectedItem, ComboBoxItem)
            If targetItem IsNot Nothing Then _settings.TemplateSaveTarget = NormalizeTemplateSaveTarget(Convert.ToString(targetItem.Tag, CultureInfo.InvariantCulture))
            If _settings.TemplateSaveTarget = "SHARED" AndAlso String.IsNullOrWhiteSpace(_settings.SharedTemplatePath) Then _settings.TemplateSaveTarget = "LOCAL"
            SyncPrintersToSettings()
            UpdatePrinterSummary()
        End Sub

        Private Sub CutterChecked(sender As Object, e As RoutedEventArgs)
            If _isInitializing OrElse _isLoadingPrinterEditor Then Return
            ShowInfo("Cutter ist eine Hardwareoption", "Der Schneidebefehl wird nur beim Druck gesendet, wenn diese Einstellung aktiv bleibt.", "Beim DH220 muss das optionale Guillotine-Cutter-Modul tatsächlich montiert sein.", True)
        End Sub

        Private Sub PrintModeChanged(sender As Object, e As RoutedEventArgs)
            If _isInitializing Then Return
            _settings.NormalPrint = Not InversePrintCheck.IsChecked.GetValueOrDefault()
            UpdatePrinterSummary()
            Dim mode = If(_settings.NormalPrint, "Normaldruck: schwarzer Text auf weißem Hintergrund.", "Inversdruck: weißer Text auf schwarzem Hintergrund.")
            SetStatus(mode, StatusKind.Neutral)
        End Sub

        Private Sub ChooseDownloadDirectoryClick(sender As Object, e As RoutedEventArgs)
            Dim dialog As New OpenFolderDialog With {
                .Title = "Ordner für Laborauftrag-Dateien auswählen",
                .InitialDirectory = If(Directory.Exists(DownloadDirectoryBox.Text), DownloadDirectoryBox.Text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            }
            If dialog.ShowDialog(Me) = True Then
                DownloadDirectoryBox.Text = dialog.FolderName
                RefreshNewestLaborFile()
                SetStatus("Download-Ordner ausgewählt; Einstellungen noch speichern.", StatusKind.Neutral)
            End If
        End Sub

        Private Sub SettingsProfileSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            Dim profile = TryCast(SettingsProfileList.SelectedItem, LabelProfile)
            If profile Is Nothing Then Return
            ProfileNameBox.Text = profile.Name
            ProfileWidthBox.Text = profile.WidthMm.ToString("0.##", CultureInfo.CurrentCulture)
            ProfileHeightBox.Text = profile.HeightMm.ToString("0.##", CultureInfo.CurrentCulture)
            ProfileGapBox.Text = profile.GapMm.ToString("0.##", CultureInfo.CurrentCulture)
            If _isInitializing OrElse _isLoadingPrinterEditor OrElse _isSynchronizingProfileSelection Then Return

            _isSynchronizingProfileSelection = True
            Try
                PrinterProfileCombo.SelectedItem = profile
            Finally
                _isSynchronizingProfileSelection = False
            End Try
            AssignPreferredProfileToSelectedPrinter(profile)
        End Sub

        Private Sub PrinterProfileForPrinterChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isInitializing OrElse _isLoadingPrinterEditor OrElse _isSynchronizingProfileSelection Then Return
            Dim profile = TryCast(PrinterProfileCombo.SelectedItem, LabelProfile)
            If profile Is Nothing Then Return
            SetSettingsProfileSelection(profile)
            AssignPreferredProfileToSelectedPrinter(profile)
        End Sub

        Private Sub AssignPreferredProfileToSelectedPrinter(profile As LabelProfile)
            Dim printer = TryCast(SettingsPrinterList.SelectedItem, PrinterProfile)
            If profile Is Nothing OrElse printer Is Nothing OrElse _editingNewPrinter Then Return

            printer.PreferredProfileId = profile.Id
            SyncPrintersToSettings()
            SettingsStore.Save(_settings)
            PrinterManagerStatusText.Text = $"Standard-Etikett gespeichert: {printer.Name} · {profile.WidthMm:0.##} × {profile.HeightMm:0.##} mm"

            If ReferenceEquals(CurrentPrinter(), printer) Then
                _settings.SelectedProfileId = profile.Id
                ProfileCombo.SelectedItem = profile
                UpdateLabelDimensions()
                ScheduleFitCheck()
                UpdatePrinterSummary()
            End If
            SetStatus($"{profile.Name} ist jetzt das Standard-Etikett für {printer.Name}.", StatusKind.Success)
        End Sub

        Private Sub AddCustomProfileClick(sender As Object, e As RoutedEventArgs)
            Try
                Dim profile = ReadProfileEditor()
                profile.Id = "custom-" & Guid.NewGuid().ToString("N")
                profile.IsCustom = True
                _profiles.Add(profile)
                _settings.CustomProfiles.Add(profile.Copy())
                ProfileCombo.SelectedItem = profile
                SettingsProfileList.SelectedItem = profile
                SettingsStore.Save(_settings)
                SetStatus("Eigenes Etikettenformat gespeichert und ausgewählt.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Format nicht gespeichert", ex.Message, "Eigene Formate sind bis 110 mm Breite möglich; der Drucker muss diese Breite unterstützen.", True)
            End Try
        End Sub

        Private Sub UpdateCustomProfileClick(sender As Object, e As RoutedEventArgs)
            Dim selected = TryCast(SettingsProfileList.SelectedItem, LabelProfile)
            If selected Is Nothing OrElse Not selected.IsCustom Then
                ShowInfo("Standardformat ist geschützt", "Bitte ein eigenes Format auswählen oder mit ‚Als eigenes Format speichern‘ eine Kopie anlegen.")
                Return
            End If
            Try
                Dim edited = ReadProfileEditor()
                selected.Name = edited.Name
                selected.WidthMm = edited.WidthMm
                selected.HeightMm = edited.HeightMm
                selected.GapMm = edited.GapMm
                SyncCustomProfilesToSettings()
                ProfileCombo.Items.Refresh()
                SettingsProfileList.Items.Refresh()
                UpdateLabelDimensions()
                SettingsStore.Save(_settings)
                SetStatus("Eigenes Etikettenformat aktualisiert.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Format nicht aktualisiert", ex.Message, "Bitte Maße prüfen.", True)
            End Try
        End Sub

        Private Sub DeleteCustomProfileClick(sender As Object, e As RoutedEventArgs)
            Dim selected = TryCast(SettingsProfileList.SelectedItem, LabelProfile)
            If selected Is Nothing OrElse Not selected.IsCustom Then
                ShowInfo("Standardformat ist geschützt", "Nur selbst angelegte Formate können entfernt werden.")
                Return
            End If
            If Not Confirm("Eigenes Format entfernen?", selected.ToString(), "Das Format wird nur aus der lokalen Konfiguration entfernt.", "Entfernen") Then Return
            Dim index = _profiles.IndexOf(selected)
            _profiles.Remove(selected)
            Dim fallback = _profiles(Math.Max(0, Math.Min(index - 1, _profiles.Count - 1)))
            For Each printer In _printers.Where(Function(item) String.Equals(item.PreferredProfileId, selected.Id, StringComparison.OrdinalIgnoreCase))
                printer.PreferredProfileId = fallback.Id
            Next
            SyncCustomProfilesToSettings()
            SyncPrintersToSettings()
            ProfileCombo.SelectedItem = fallback
            SettingsProfileList.SelectedItem = fallback
            SettingsStore.Save(_settings)
            SetStatus("Eigenes Format entfernt.", StatusKind.Success)
        End Sub

        Private Function ReadProfileEditor() As LabelProfile
            Dim widthValue, heightValue, gapValue As Double
            If String.IsNullOrWhiteSpace(ProfileNameBox.Text) Then Throw New InvalidOperationException("Eine Bezeichnung fehlt.")
            If Not TryParseDouble(ProfileWidthBox.Text, widthValue) OrElse widthValue < 10 OrElse widthValue > 110 Then Throw New InvalidOperationException("Breite muss zwischen 10 und 110 mm liegen.")
            If Not TryParseDouble(ProfileHeightBox.Text, heightValue) OrElse heightValue < 10 OrElse heightValue > 300 Then Throw New InvalidOperationException("Höhe muss zwischen 10 und 300 mm liegen.")
            If Not TryParseDouble(ProfileGapBox.Text, gapValue) OrElse gapValue < 0 OrElse gapValue > 10 Then Throw New InvalidOperationException("GAP muss zwischen 0 und 10 mm liegen.")
            Return New LabelProfile With {.Name = ProfileNameBox.Text.Trim(), .WidthMm = widthValue, .HeightMm = heightValue, .GapMm = gapValue, .IsCustom = True}
        End Function

        Private Sub SyncCustomProfilesToSettings()
            _settings.CustomProfiles = _profiles.Where(Function(profile) profile.IsCustom).Select(Function(profile) profile.Copy()).ToList()
        End Sub

        Private Sub RefreshHistory()
            Try
                Dim selectedId = TryCast(HistoryList.SelectedItem, PrintHistoryEntry)?.Id
                _historyEntries.Clear()
                For Each entry In PrintHistoryStore.Load()
                    _historyEntries.Add(entry)
                Next
                If selectedId IsNot Nothing Then HistoryList.SelectedItem = _historyEntries.FirstOrDefault(Function(entry) entry.Id = selectedId)
                If HistoryList.SelectedItem Is Nothing AndAlso _historyEntries.Count > 0 Then HistoryList.SelectedIndex = 0
                If _historyEntries.Count = 0 Then
                    HistoryDetailsText.Text = "Noch keine gespeicherten Ausdrucke."
                    HistoryModeText.Text = ""
                End If
            Catch ex As Exception
                _historyEntries.Clear()
                HistoryDetailsText.Text = "Verlauf konnte nicht gelesen werden."
                SetStatus("Portable Verlaufsdatei konnte nicht gelesen werden; sie wurde nicht verändert.", StatusKind.Warning)
            End Try
        End Sub

        Private Sub HistorySelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            Dim entry = TryCast(HistoryList.SelectedItem, PrintHistoryEntry)
            If entry Is Nothing Then Return
            Try
                HistoryPreviewEditor.Document = PrintHistoryStore.RestoreDocument(entry)
                HistoryPreviewEditor.Width = entry.WidthMm / 25.4 * 96.0
                HistoryPreviewEditor.Height = entry.HeightMm / 25.4 * 96.0
                HistoryPreviewEditor.Padding = New Thickness(_settings.MarginMm / 25.4 * 96.0)
                Dim printerInfo = If(String.IsNullOrWhiteSpace(entry.PrinterName), "", $" · {entry.PrinterName} ({entry.PrinterLanguage})")
                HistoryDetailsText.Text = $"{entry.ProfileName} · {entry.WidthMm:0.##} × {entry.HeightMm:0.##} mm · {entry.Copies} Exemplar(e){printerInfo}"
                HistoryModeText.Text = If(entry.NormalPrint, "Normaldruck", "Inversdruck")
            Catch ex As Exception
                HistoryDetailsText.Text = ex.Message
                HistoryModeText.Text = "Fehler"
            End Try
        End Sub

        Private Sub RestoreHistoryClick(sender As Object, e As RoutedEventArgs)
            Dim entry = TryCast(HistoryList.SelectedItem, PrintHistoryEntry)
            If entry Is Nothing Then
                ShowInfo("Kein Ausdruck ausgewählt", "Bitte links zuerst ein gespeichertes Etikett auswählen.")
                Return
            End If
            Try
                SetLabelDocument(PrintHistoryStore.RestoreDocument(entry))
                Dim matchingProfile = _profiles.FirstOrDefault(Function(profile) Math.Abs(profile.WidthMm - entry.WidthMm) < 0.01 AndAlso Math.Abs(profile.HeightMm - entry.HeightMm) < 0.01)
                If matchingProfile IsNot Nothing Then ProfileCombo.SelectedItem = matchingProfile
                CopiesCombo.SelectedIndex = Math.Clamp(entry.Copies, 1, 3) - 1
                InversePrintCheck.IsChecked = Not entry.NormalPrint
                RootTabs.SelectedIndex = 0
                SetStatus("Gespeichertes Etikett in den Editor übernommen – noch nicht gedruckt.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Etikett konnte nicht übernommen werden", ex.Message, "Der Verlaufseintrag wurde nicht verändert.", True)
            End Try
        End Sub

        Private Sub ClearHistoryClick(sender As Object, e As RoutedEventArgs)
            If _historyEntries.Count = 0 Then
                SetStatus("Der Druckverlauf ist bereits leer.", StatusKind.Neutral)
                Return
            End If
            If Not Confirm("Druckverlauf wirklich leeren?", $"{_historyEntries.Count} gespeicherte Etiketten werden aus der portablen JSON-Datei entfernt.", PrintHistoryStore.DefaultPath, "Verlauf leeren") Then Return
            Try
                PrintHistoryStore.Clear()
                _historyEntries.Clear()
                HistoryPreviewEditor.Document = LabelDocumentFactory.CreateBaseDocument()
                HistoryDetailsText.Text = "Noch keine gespeicherten Ausdrucke."
                HistoryModeText.Text = ""
                SetStatus("Druckverlauf geleert.", StatusKind.Success)
            Catch ex As Exception
                ShowInfo("Verlauf konnte nicht geleert werden", ex.Message, PrintHistoryStore.DefaultPath, True)
            End Try
        End Sub

        Private Sub UpdatePrinterSummary()
            If _settings Is Nothing Then Return
            Dim printer = CurrentPrinter()
            If printer Is Nothing Then
                PrintSummaryText.Text = "Kein Zieldrucker gewählt"
                Return
            End If
            Dim cutter = If(printer.UsesWindowsRaster, "", If(printer.CutterEnabled, " · Cutter", " · ohne Cutter"))
            Dim printMode = If(InversePrintCheck IsNot Nothing AndAlso InversePrintCheck.IsChecked.GetValueOrDefault(), "Invers", "Normal")
            Dim profile = CurrentProfile()
            Dim formatText = If(profile Is Nothing, "", $" · {profile.WidthMm:0.##} × {profile.HeightMm:0.##} mm")
            Dim rotationText = If(printer.UsesWindowsRaster AndAlso printer.RasterRotationDegrees <> 0, $" · Treiberdrehung {printer.RasterRotationDegrees}°", "")
            PrintSummaryText.Text = $"{printMode} · {printer.Name} · {printer.ConnectionText}{formatText}{rotationText}{cutter}"
            If TargetPrinterSettingsText IsNot Nothing Then TargetPrinterSettingsText.Text = "Aktiver Zieldrucker: " & printer.Name
        End Sub

        Private Function LoadCommandLineInput() As Boolean
            Dim args = Environment.GetCommandLineArgs()
            If BrainOfficeLabelCommand.IsV1Command(args.Skip(1)) Then Return True
            For index = 1 To args.Length - 2
                If String.Equals(args(index), "--laborauftrag", StringComparison.OrdinalIgnoreCase) Then
                    LoadLaborFile(args(index + 1))
                    Return True
                End If
                If String.Equals(args(index), "--patient-json", StringComparison.OrdinalIgnoreCase) Then
                    Try
                        Dim data = JsonSerializer.Deserialize(Of PatientLabelData)(File.ReadAllText(args(index + 1)), New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True})
                        If data Is Nothing Then Throw New InvalidDataException("Patienten-JSON ist leer.")
                        Dim templateName = ApplyPatientDataToStoredTemplate(data)
                        SetStatus($"Patientendaten über die Brain-Assistent-Schnittstelle in Vorlage {templateName} übernommen.", StatusKind.Success)
                    Catch ex As Exception
                        ShowInfo("Patientendaten konnten nicht übernommen werden", ex.Message, "Bitte eine lokale Vorlage mit Patienten-Platzhaltern wählen.", True)
                    End Try
                    Return True
                End If
            Next
            Return False
        End Function

        Public Function ProcessBrainOfficeTemplateLaunch(command As BrainOfficeLabelCommand) As BrainOfficeTemplateLaunchResult
            If command Is Nothing Then Throw New ArgumentNullException(NameOf(command))
            Dim cached As BrainOfficeTemplateLaunchResult = Nothing
            If Not String.IsNullOrWhiteSpace(command.RequestId) AndAlso _brainOfficeLaunchResults.TryGetValue(command.RequestId, cached) Then
                BrainOfficeResultWriter.WriteAtomic(command.ResultPath, cached)
                Return cached
            End If

            Dim result As New BrainOfficeTemplateLaunchResult With {.RequestId = command.RequestId}
            Try
                If command.Mode <> BrainOfficeLabelCommandMode.OpenTemplate Then Throw New InvalidOperationException("Unbekannter BrainOffice-Auftrag.")
                If command.AutoPrint Then Throw New InvalidOperationException("Autodruck ist für Schnellwahlen nicht erlaubt.")
                If _isTemplateDesignMode Then Throw New InvalidOperationException("Vorlagenentwurf ist noch geöffnet. Bitte zuerst speichern oder verwerfen.")

                Dim template = _allTemplates.FirstOrDefault(Function(item) String.Equals(item.Id, command.TemplateId, StringComparison.OrdinalIgnoreCase))
                If template Is Nothing Then Throw New InvalidOperationException("Etikettenvorlage nicht gefunden. Bitte Schnellwahlen aktualisieren.")
                result.TemplateResolved = True
                result.TemplateId = template.Id
                result.TemplateName = template.Name

                Dim printer = _printers.FirstOrDefault(Function(item) String.Equals(item.Id, template.PrinterId, StringComparison.OrdinalIgnoreCase))
                If printer Is Nothing Then Throw New InvalidOperationException($"Drucker '{template.PrinterName}' ist auf diesem Arbeitsplatz nicht verfügbar. Es wurde nichts gedruckt.")
                result.PrinterName = printer.Name

                Dim profile = _profiles.FirstOrDefault(Function(item) String.Equals(item.Id, template.ProfileId, StringComparison.OrdinalIgnoreCase))
                If profile Is Nothing Then profile = _profiles.FirstOrDefault(Function(item) Math.Abs(item.WidthMm - template.WidthMm) < 0.01 AndAlso Math.Abs(item.HeightMm - template.HeightMm) < 0.01)
                If profile Is Nothing Then Throw New InvalidOperationException("Das gespeicherte Etikettenformat ist auf diesem Arbeitsplatz nicht verfügbar.")
                Dim patient As PatientLabelData
                Try
                    If Not File.Exists(command.PatientJsonPath) OrElse New FileInfo(command.PatientJsonPath).Length > 1024 * 1024 Then Throw New InvalidDataException()
                    patient = JsonSerializer.Deserialize(Of PatientLabelData)(File.ReadAllText(command.PatientJsonPath), New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True})
                    If patient Is Nothing Then Throw New InvalidDataException()
                Catch
                    Throw New InvalidDataException("Patientendaten konnten nicht übernommen werden. Etikett wurde nicht geöffnet.")
                End Try

                Dim requestedDocument = LabelTemplateStore.RestoreDocument(template)
                If Not LabelTemplateEngine.ContainsPlaceholders(requestedDocument) Then Throw New InvalidDataException("Die angeforderte Vorlage enthält keine Patienten-Platzhalter. Etikett wurde nicht geöffnet.")
                Dim replacements = LabelTemplateEngine.ApplyPatientData(requestedDocument, patient, "", PracticeLineBox.Text)
                If replacements = 0 Then Throw New InvalidDataException("Die angeforderte Vorlage enthält keine Patienten-Platzhalter. Etikett wurde nicht geöffnet.")

                _isApplyingTemplate = True
                Try
                    TargetPrinterCombo.SelectedItem = printer
                    ProfileCombo.SelectedItem = profile
                    SetLabelDocument(requestedDocument)
                    _activeTemplate = template
                    SetSelectedTemplate(template)
                    _settings.LastUsedTemplateId = template.Id
                    _settings.SelectedPrinterId = printer.Id
                    _settings.SelectedProfileId = profile.Id
                    SettingsStore.Save(_settings)
                Finally
                    _isApplyingTemplate = False
                End Try

                RootTabs.SelectedIndex = 0
                If WindowState = WindowState.Minimized Then WindowState = WindowState.Normal
                Show()
                Topmost = True
                Topmost = False
                Dim foregrounded = Activate()
                If IsHandleCreated() Then foregrounded = SetForegroundWindow(New System.Windows.Interop.WindowInteropHelper(Me).Handle) OrElse foregrounded
                LabelEditor.Focus()
                SetStatus($"Vorlage '{template.Name}' bereit · {printer.Name} · {profile.WidthMm:0.##} × {profile.HeightMm:0.##} mm · bitte prüfen und drucken.", StatusKind.Success)
                result.Accepted = True
                result.Message = If(foregrounded, "Vorlage im Etikettenmaster geöffnet – noch nicht gedruckt.", "Vorlage wurde vorbereitet, Fenster konnte nicht in den Vordergrund geholt werden.")
            Catch ex As Exception
                result.Accepted = False
                result.Message = ex.Message
                SetStatus(ex.Message, StatusKind.Warning)
                If WindowState = WindowState.Minimized Then WindowState = WindowState.Normal
                Show()
                Activate()
            End Try

            If Not String.IsNullOrWhiteSpace(command.RequestId) Then
                _brainOfficeLaunchResults(command.RequestId) = result
                While _brainOfficeLaunchResults.Count > 100
                    _brainOfficeLaunchResults.Remove(_brainOfficeLaunchResults.Keys.First())
                End While
            End If
            BrainOfficeResultWriter.WriteAtomic(command.ResultPath, result)
            Return result
        End Function

        Private Function IsHandleCreated() As Boolean
            Return New System.Windows.Interop.WindowInteropHelper(Me).Handle <> IntPtr.Zero
        End Function

        Private Function TryParseDouble(textValue As String, ByRef value As Double) As Boolean
            Return Double.TryParse(textValue, NumberStyles.Float, CultureInfo.CurrentCulture, value) OrElse Double.TryParse(textValue, NumberStyles.Float, CultureInfo.InvariantCulture, value)
        End Function

        Private Function Confirm(titleValue As String, messageValue As String, contextValue As String, confirmText As String) As Boolean
            Dim dialog As New AppDialogWindow With {.Owner = Me}
            dialog.Configure(titleValue, messageValue, contextValue, confirmText, "Abbrechen", True, True)
            Return dialog.ShowDialog().GetValueOrDefault()
        End Function

        Private Sub ShowInfo(titleValue As String, messageValue As String, Optional contextValue As String = "", Optional warning As Boolean = False)
            Dim dialog As New AppDialogWindow With {.Owner = Me}
            dialog.Configure(titleValue, messageValue, contextValue, "OK", "", False, warning)
            dialog.ShowDialog()
        End Sub

        Private Enum StatusKind
            Neutral
            Busy
            Success
            Warning
            ErrorState
        End Enum

        Private Sub SetStatus(message As String, kind As StatusKind)
            If ActiveTemplateText IsNot Nothing Then
                ActiveTemplateText.Text = If(_activeTemplate Is Nothing, "Neuer Entwurf", "Aktiv: " & _activeTemplate.Name)
                ActiveTemplateText.ToolTip = ActiveTemplateText.Text
                ActiveTemplatePrinterText.Text = If(_activeTemplate Is Nothing, "Standarddrucker: aktueller Zieldrucker", "Standarddrucker: " & _activeTemplate.PrinterName)
            End If
            StatusText.Text = message
            Select Case kind
                Case StatusKind.Success
                    StatusDot.Fill = New SolidColorBrush(Color.FromRgb(19, 122, 82))
                    StatusText.Foreground = New SolidColorBrush(Color.FromRgb(19, 122, 82))
                Case StatusKind.Warning
                    StatusDot.Fill = New SolidColorBrush(Color.FromRgb(182, 106, 19))
                    StatusText.Foreground = New SolidColorBrush(Color.FromRgb(150, 84, 12))
                Case StatusKind.ErrorState
                    StatusDot.Fill = New SolidColorBrush(Color.FromRgb(188, 55, 55))
                    StatusText.Foreground = New SolidColorBrush(Color.FromRgb(163, 43, 43))
                Case StatusKind.Busy
                    StatusDot.Fill = New SolidColorBrush(Color.FromRgb(37, 99, 235))
                    StatusText.Foreground = New SolidColorBrush(Color.FromRgb(37, 99, 235))
                Case Else
                    StatusDot.Fill = New SolidColorBrush(Color.FromRgb(15, 111, 115))
                    StatusText.Foreground = New SolidColorBrush(Color.FromRgb(100, 117, 125))
            End Select
        End Sub
    End Class
End Namespace
