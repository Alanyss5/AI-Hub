using AIHub.Contracts;

namespace AIHub.Desktop.Text;

public sealed class DesktopTextCatalog
{
    public static DesktopTextCatalog Default { get; } = new();

    public ShellText Shell { get; } = new();

    public ProjectsText Projects { get; } = new();

    public SkillsText Skills { get; } = new();

    public ScriptsText Scripts { get; } = new();

    public McpText Mcp { get; } = new();

    public SettingsText Settings { get; } = new();

    public DialogsText Dialogs { get; } = new();

    public StateText State { get; } = new();

    public sealed class ShellText
    {
        public string AppTitle => "AI-Hub 控制台";

        public string HeroTitle => "AI-Hub 控制台";

        public string HeroSubtitle => "统一管理 Skills、Scripts、Projects 与 MCP 的桌面控制台。";

        public string ActiveScopeLabel => "当前作用域";

        public string RefreshAllButton => "刷新全部";

        public string TrayShowWindowMenu => "显示主窗口";

        public string TrayRefreshStatusMenu => "刷新状态";

        public string TrayStartAllMcpMenu => "启动全部 MCP";

        public string TrayStopAllMcpMenu => "停止全部 MCP";

        public string TrayMaintainMenu => "执行自恢复";

        public string TrayExitMenu => "退出";

        public string OverviewTabHeader => "概览";

        public string HubWorkspaceTitle => "Hub 工作区";

        public string DefaultProfileLabel => "默认 Profile";

        public string RecentProjectLabel => "最近项目";

        public string PreferredClientsTitle => "首选客户端";

        public string EnabledModulesTitle => "已启用模块";

        public string NextStepsTitle => "后续重点";

        public string ValidationHintsTitle => "当前校验提示";

        public string RecentOperationsTitle => "最近操作";

        public string VersionDisplay(string version) => "版本 " + version;
    }

    public sealed class ProjectsText
    {
        public string TabHeader => "项目与 Profile";

        public string ListTitle => "项目列表";

        public string RefreshButton => "刷新";

        public string FormTitle => "项目表单";

        public string NameLabel => "项目名称";

        public string NameWatermark => "例如：OverSeaFramework";

        public string DirectoryLabel => "项目目录";

        public string DirectoryWatermark => "选择本地项目目录";

        public string ChooseDirectoryButton => "选择目录";

        public string ProfileLabel => "Profile";

        public string PinLabel => "置顶显示";

        public string WorkspaceStatusTitle => "当前工作区状态";

        public string RuntimeIdentityTitle => "运行标识";

        public string BuildSourceLabel => "构建来源";

        public string ExecutablePathLabel => "可执行文件";

        public string WorkspaceBindingTitle => "当前接管状态";

        public string WorkspaceBindingModeLabel => "接管模式";

        public string ScopeLabel => "作用域";

        public string ActionCenterTitle => "操作中心";

        public string SaveProjectButton => "新增或更新项目";

        public string ApplySelectedProfileButton => "应用所选 Profile";

        public string SetCurrentProjectButton => "设为当前项目";

        public string SwitchProjectScopeButton => "切换为项目级";

        public string SwitchGlobalScopeButton => "切换为全局级";

        public string ApplyGlobalLinksButton => "应用全局链接";

        public string RescanGlobalOnboardingButton => "重新扫描全局接管";

        public string RescanProjectOnboardingButton => "重新扫描项目接管";

        public string GlobalOnboardingLabel => "全局接管状态";

        public string ProjectOnboardingLabel => "项目接管状态";

        public string DeleteSelectedButton => "删除所选";

        public string ClearFormButton => "清空表单";

        public string ResultTitle => "执行结果";
    }

    public sealed class SkillsText
    {
        public string TabHeader => "Skills";

        public string InstalledTitle => "已安装 Skills";

        public string SourcesTitle => "来源列表";

        public string SourceEditorTitle => "来源编辑器";

        public string SourceNameLabel => "来源名称";

        public string SourceNameWatermark => "例如：developer-essentials";

        public string ScopeLabel => "作用域";

        public string SourceTypeLabel => "来源类型";

        public string SourceAddressLabel => "来源地址";

        public string SourceAddressWatermark => "Git 仓库地址或本地目录路径";

        public string PathLabel => "目录 / 技能路径";

        public string PathWatermark => "可选；记录来源中的目录或技能路径";

        public string ReferenceLabel => "Reference";

                public string VersionTrackingFollowLatestStableTagOption => "跟踪最新稳定标签";

        public string VersionTrackingPinTagOption => "固定标签";

        public string VersionTrackingFollowReferenceLegacyOption => "传统引用模式";

        public string SourceEnabledLabel => "启用该来源";

        public string ScheduledUpdateEnabledLabel => "启用运行期定时更新";

        public string ScheduleTitle => "来源级定时策略";

        public string ScheduleIntervalLabel => "执行频率";

        public string ScheduleActionLabel => "执行动作";

        public string ScheduleStatusLabel => "运行状态";

        public string RunScheduleNowButton => "立即执行该来源策略";

        public string ScheduleIntervalOffOption => "关闭";

        public string ScheduleInterval6HoursOption => "每 6 小时";

        public string ScheduleInterval12HoursOption => "每 12 小时";

        public string ScheduleInterval24HoursOption => "每 24 小时";

        public string ScheduleInterval7DaysOption => "每 7 天";

        public string ScheduledActionCheckOnlyOption => "仅检查";

        public string ScheduledActionCheckAndSyncSafeOption => "检查并安全同步";

        public string ScanResultTitle => "扫描结果";

        public string SaveSourceButton => "保存来源";

        public string ScanSourceButton => "扫描来源";

        public string DeleteSourceButton => "删除来源";

        public string ClearSourceFormButton => "清空表单";

        public string InstallRecordTitle => "安装登记";

        public string CustomizationModeLabel => "自定义模式";

        public string BindSourceLabel => "绑定来源";

        public string SourceSkillPathLabel => "来源中的技能路径";

        public string SourceSkillPathWatermark => "可选；仅登记当前安装项对应的技能路径";

        public string SaveInstallButton => "保存登记";

        public string DeleteInstallButton => "删除登记";

        public string CaptureBaselineButton => "采集基线";

        public string PreviewDiffButton => "预览差异";

        public string PreviewMergeButton => "预览合并";

        public string ApplyMergeButton => "应用合并";

        public string CheckUpdatesButton => "检查更新";

        public string SafeSyncButton => "安全同步";

        public string ForceSyncButton => "强制同步";

        public string RollbackButton => "回滚";

        public string CurrentStatusTitle => "当前状态";

        public string BackupsTitle => "备份记录";

        public string MergeTitle => "Overlay 合并";

        public string MergeHint => "说明：只有干净的托管 Skill 适合自动更新；检测到本地修改时，应先转为 Overlay、Fork 或本地模式。";

        public string MergeStatusSourceOnly => "来源新增";

        public string MergeStatusLocalOnly => "仅本地改动";

        public string MergeStatusSourceChanged => "来源更新";

        public string MergeStatusSourceDeleted => "来源删除";

        public string MergeStatusConflict => "冲突";

        public string MergeStatusPending => "待处理";

        public string MergeDecisionUseSourceOption => "采用来源版";

        public string MergeDecisionKeepLocalOption => "保留本地版";

        public string MergeDecisionApplyDeletionOption => "按删除状态应用";

        public string MergeDecisionSkipOption => "跳过";
    }

    public sealed class ScriptsText
    {
        public string TabHeader => "脚本中心";

        public string ListTitle => "脚本清单";

        public string DescriptionTitle => "脚本说明";

        public string ProjectDirectoryLabel => "项目目录";

        public string ProjectDirectoryWatermark => "脚本需要项目目录时填写";

        public string UserDirectoryLabel => "用户目录";

        public string UserDirectoryWatermark => "setup-global.ps1 需要时填写";

        public string ChooseDirectoryButton => "选择目录";

        public string ProfileLabel => "Profile";

        public string ArgumentsLabel => "自定义参数";

        public string ArgumentsWatermark => "例如：-Verbose 或自定义开关";

        public string ExecuteButton => "运行所选脚本";

        public string RefreshButton => "刷新状态";

        public string OutputTitle => "脚本输出";
    }

    public sealed class McpText
    {
        public string TabHeader => "MCP 管理";

        public string RuntimeOverviewTitle => "运行中心概览";

        public string ManagedProcessesTitle => "托管进程列表";

        public string ManagedProcessesDescription => "支持启动、停止、重启、日志查看与健康检查。";

        public string ProfileListTitle => "Profile 列表";

        public string ProfileListDescription => "选择一个 Profile 编辑其 Manifest，并查看三端生成结果。";

        public string ManagedProcessTabHeader => "托管进程";

        public string ConfigTabHeader => "配置与生成";

        public string CurrentStatusTitle => "当前状态";

        public string HealthStatusTitle => "健康状态";

        public string NameLabel => "名称";

        public string NameWatermark => "例如：my-http-mcp";

        public string StartCommandLabel => "启动命令";

        public string StartCommandWatermark => "例如：node 或 dotnet";

        public string ArgumentsLabel => "参数";

        public string ArgumentsWatermark => "例如：dist/server.js --port 8787";

        public string WorkingDirectoryLabel => "工作目录";

        public string WorkingDirectoryWatermark => "可选；为空则使用默认工作目录";

        public string ChooseDirectoryButton => "选择目录";

        public string ManagedProcessEnabledLabel => "启用该托管进程";

        public string ManagedProcessAutoStartLabel => "应用启动时自动启动";

        public string ManagedProcessKeepAliveLabel => "异常退出后保持拉起";

        public string HealthCheckAddressLabel => "健康检查地址";

        public string HealthCheckAddressWatermark => "可选；例如：http://127.0.0.1:8787/health";

        public string HealthCheckTimeoutLabel => "超时（秒）";

        public string SaveDefinitionButton => "保存定义";

        public string StartButton => "启动";

        public string StopButton => "停止";

        public string RestartButton => "重启";

        public string HealthCheckButton => "健康检查";

        public string StartAllButton => "启动全部";

        public string StopAllButton => "停止全部";

        public string MaintainButton => "执行自恢复";

        public string DeleteDefinitionButton => "删除定义";

        public string ClearFormButton => "清空表单";

        public string LogPreviewTitle => "日志预览";

        public string StandardOutputTabHeader => "标准输出";

        public string StandardErrorTabHeader => "标准错误";

        public string CurrentProfileTitle => "当前 Profile";

        public string SaveManifestButton => "保存 Manifest";

        public string GenerateConfigsButton => "生成配置";

        public string ValidateScopeButton => "体检当前作用域";

        public string SyncClientsButton => "同步客户端";

        public string ManifestEditorTitle => "Manifest 编辑器";

        public string ValidationResultsTitle => "MCP 体检结果";

        public string ExternalImportTitle => "外部 MCP 纳管";

        public string ExternalImportDescription => "识别客户端配置中存在但尚未纳入 AI-Hub Manifest 的服务，支持按客户端定义导入并同步。";

        public string SyncImportedClientsLabel => "导入后立即同步当前作用域客户端";

        public string ImportSelectedButton => "导入所选";

        public string GeneratedPreviewTitle => "生成结果预览";
    }

    public sealed class SettingsText
    {
        public string TabHeader => "设置";

        public string HubRootTitle => "AI-Hub 根目录";

        public string HubRootWatermark => "选择或输入新的 AI-Hub 根目录";

        public string ChooseDirectoryButton => "选择目录";

        public string SwitchRootButton => "切换根目录";

        public string ConsoleSettingsTitle => "控制台设置";

        public string AutoStartManagedMcpLabel => "启动控制台时自动启动已标记为自启动的 MCP";

        public string AutoCheckSkillsLabel => "启动控制台时自动检查 Skills 更新";

        public string AutoSyncSkillsLabel => "启动控制台时自动同步安全的托管 / Overlay Skill";

        public string SaveSettingsButton => "保存设置";

        public string SwitchGlobalScopeButton => "切换为全局级";

        public string SwitchCurrentProjectScopeButton => "切换为当前项目级";

        public string DiagnosticsTitle => "诊断与恢复";

        public string DiagnosticsDescription => "记录启动失败、未处理异常和关键后台操作，可导出诊断包供内部排障。";

        public string DiagnosticsSummaryTitle => "诊断摘要";

        public string DiagnosticsRootLabel => "诊断目录";

        public string LatestStartupFailureTitle => "最近一次启动失败";

        public string LatestUnhandledExceptionTitle => "最近一次未处理异常";

        public string DiagnosticsPathWatermark => "选择诊断包导出路径";

        public string ChooseDiagnosticsExportButton => "选择诊断包路径";

        public string ExportDiagnosticsButton => "导出诊断包";

        public string SecurityTitle => "信任边界";

        public string SecurityDescription => "脚本执行、托管 MCP 运行和外部 MCP 导入首次都需要显式确认。";

        public string ScriptRiskTitle => "脚本执行";

        public string ManagedMcpRiskTitle => "托管 MCP 运行";

        public string ExternalMcpRiskTitle => "外部 MCP 导入";

        public string ResetRiskConfirmationsButton => "重置风险确认";

        public string PackageTitle => "配置包导入 / 导出";

        public string PackageDescription => "可导出当前 Hub 的项目、Settings、Skills 与 MCP 配置，并支持重新导入恢复。";

        public string PackagePathWatermark => "选择或输入配置包文件路径";

        public string ChooseExportButton => "选择导出路径";

        public string ChooseImportButton => "选择导入文件";

        public string ExportButton => "导出配置包";

        public string ImportButton => "导入配置包";
    }

    public sealed class DialogsText
    {
        public string SelectProjectDirectory => "选择项目目录";

        public string SelectHubRootDirectory => "选择 AI-Hub 根目录";

        public string SelectScriptProjectDirectory => "选择脚本项目目录";

        public string SelectUserDirectory => "选择用户目录";

        public string SelectManagedProcessWorkingDirectory => "选择托管进程工作目录";

        public string SelectPackageSaveLocation => "选择配置包导出位置";

        public string SelectPackageToImport => "选择要导入的配置包";

        public string PackageFileTypeName => "AI-Hub 配置包";

        public string SelectDiagnosticsExportLocation => "选择诊断包导出位置";

        public string DiagnosticsFileTypeName => "AI-Hub 诊断包";

        public string DeleteProjectTitle => "删除项目";

        public string DeleteProjectMessage => "将从 AI-Hub 项目注册表中移除该项目，不会删除磁盘上的项目目录。";

        public string DeleteProjectConfirmText => "删除项目";

        public string DeleteManagedProcessTitle => "删除托管 MCP";

        public string DeleteManagedProcessMessage => "将删除该托管进程定义；如果进程仍在运行，系统会先尝试停止它。";

        public string DeleteManagedProcessConfirmText => "删除定义";

        public string DeleteSkillSourceTitle => "删除 Skills 来源";

        public string DeleteSkillSourceMessage => "将移除该来源记录；已安装 Skill 的目录内容不会被直接删除。";

        public string DeleteSkillSourceConfirmText => "删除来源";

        public string DeleteSkillInstallTitle => "删除 Skill 登记";

        public string DeleteSkillInstallMessage => "将移除此 Skill 的安装登记与同步基线，但不会删除磁盘上的技能目录。";

        public string DeleteSkillInstallConfirmText => "删除登记";

        public string StopAllManagedProcessesTitle => "停止全部托管 MCP";

        public string StopAllManagedProcessesMessage => "将停止当前 AI-Hub 中全部仍在运行的托管型 MCP 进程。";

        public string StopAllManagedProcessesConfirmText => "停止全部";

        public string ForceSyncSkillTitle => "强制同步 Skill";

        public string ForceSyncSkillMessage => "强制同步会用来源内容覆盖当前目录，并在执行前创建备份。";

        public string ForceSyncSkillConfirmText => "执行强制同步";

        public string RollbackSkillTitle => "回滚 Skill";

        public string RollbackSkillMessage => "将使用所选备份覆盖当前 Skill 目录，并先为当前内容创建一份回滚前备份。";

        public string RollbackSkillConfirmText => "执行回滚";

        public string OverlayMergeTitle => "应用 Overlay 合并";

        public string OverlayMergeMessage => "将按当前文件级决策应用来源变更，并在执行前创建 pre-merge 备份。";

        public string OverlayMergeConfirmText => "应用合并";

        public string ImportConfigurationPackageTitle => "导入配置包";

        public string ImportConfigurationPackageMessage => "导入将覆盖当前 Hub 的设置、项目、Skills 与 MCP 配置，并先创建备份。";

        public string ImportConfigurationPackageConfirmText => "开始导入";

        public string ImportExternalMcpTitle => "导入外部 MCP";

        public string ImportExternalMcpWithSyncMessage => "将按当前选项导入外部 MCP，并在导入后同步到当前作用域客户端。";

        public string ImportExternalMcpWithoutSyncMessage => "将按当前选项导入外部 MCP 到 AI-Hub Manifest。";

        public string ImportExternalMcpConfirmText => "开始导入";

        public string ConfirmScriptRiskTitle => "确认脚本执行风险";

        public string ConfirmScriptRiskMessage => "脚本可能修改链接、配置文件或项目目录。确认后，AI-Hub 才会继续调用 PowerShell 执行脚本。";

        public string ConfirmScriptRiskConfirmText => "确认脚本执行";

        public string[] ScriptRiskDetails =>
        [
            "影响范围：PowerShell 脚本执行",
            "可能动作：写入链接、修改配置、变更项目目录"
        ];

        public string ConfirmManagedMcpRiskTitle => "确认托管 MCP 风险";

        public string ConfirmManagedMcpRiskMessage => "托管 MCP 会在本机启动进程、写入日志并按健康检查结果进行自恢复。确认后才允许启动或自动拉起。";

        public string ConfirmManagedMcpRiskConfirmText => "确认托管 MCP 运行";

        public string[] ManagedMcpRiskDetails =>
        [
            "影响范围：本机拉起托管 MCP 进程",
            "可能动作：写日志、健康检查、自恢复重启"
        ];

        public string ConfirmExternalMcpRiskTitle => "确认外部 MCP 导入风险";

        public string ConfirmExternalMcpRiskMessage => "导入外部 MCP 会把客户端现有定义纳入 AI-Hub 管理，并可能同步到其他客户端。";

        public string ConfirmExternalMcpRiskConfirmText => "确认外部 MCP 导入";

        public string[] ExternalMcpRiskDetails =>
        [
            "影响范围：客户端现有 MCP 配置",
            "可能动作：导入 Manifest、同步到其他客户端"
        ];

        public string ResetRiskConfirmationsTitle => "重置风险确认";

        public string ResetRiskConfirmationsMessage => "重置后，脚本执行、托管 MCP 运行和外部 MCP 导入都需要再次显式确认。";

        public string ResetRiskConfirmationsConfirmText => "重置确认";

        public string NoticeConfirmText => "知道了";

        public string GlobalOnboardingDialogTitle => "全局接管向导";

        public string ProjectOnboardingDialogTitle => "项目接管向导";

        public string ProjectPathMismatchTitle => "项目路径尚未保存";

        public string ProjectPathMismatchMessage => "当前登记路径与表单目录不一致。接管、设为当前项目和项目重扫前，请先保存项目。";

        public string RescanResultTitle => "重新扫描结果";
    }

    public sealed class StateText
    {
        public string HubRootNotResolved => "未解析到 Hub 根目录。";

        public string WaitingForWorkspace => "等待加载 AI-Hub 工作区。";

        public string NotSet => "未设置";

        public string WaitingForWorkspaceData => "等待加载工作区数据。";

        public string OperationDetailsPlaceholder => "操作详情和脚本输出会显示在这里。";

        public string NoManagedMcpDefinitions => "当前还没有托管型 MCP 定义。";

        public string NotSelected => "未选择";

        public string McpConfigNotLoaded => "尚未载入 MCP 配置。";

        public string NotGenerated => "未生成";

        public string ConfigNotGenerated => "尚未生成配置。";

        public string NoManagedProcessSelected => "未选择托管进程。";

        public string NotChecked => "未检查";

        public string SaveDefinitionBeforeOperate => "保存定义后即可启动、停止或重启。";

        public string NoLogs => "暂无日志。";

        public string SelectScript => "请选择脚本。";

        public string SelectInstalledSkill => "请选择左侧已安装 Skill。";

        public string SkillInstallStatusPlaceholder => "尚未登记来源与更新策略。";

        public string SkillInstallBaselinePlaceholder => "尚未建立基线。";

        public string SkillInstallSourcePlaceholder => "尚未绑定来源。";

        public string ScriptUsagePlaceholder => "脚本说明会显示在这里。";

        public string HintPrefix => "提示：";

        public string NoStatusMessage => "暂无状态消息。";

        public string WorkspaceRefreshed => "工作区状态已刷新。";

        public string ProjectFormCleared => "项目表单已清空。";

        public string ManagedProcessFormCleared => "托管进程表单已清空。";

        public string SkillSourceFormCleared => "Skills 来源表单已清空。";

        public string SelectMcpProfileFirst => "请先选择要编辑的 MCP Profile。";

        public string SelectManagedProcessToStart => "请先选择要启动的托管进程。";

        public string SelectManagedProcessToStop => "请先选择要停止的托管进程。";

        public string SelectManagedProcessToRestart => "请先选择要重启的托管进程。";

        public string SelectManagedProcessToHealthCheck => "请先选择要检查的托管进程。";

        public string SelectScriptFirst => "请先选择要运行的脚本。";

        public string EnterHubRootFirst => "请先填写 AI-Hub 根目录。";

        public string UnhandledError => "执行过程中出现异常。";

        public string ProjectNameRequired => "项目名称不能为空。";

        public string ProjectPathRequired => "项目路径不能为空。";

        public string ManagedProcessNameRequired => "托管进程名称不能为空。";

        public string ManagedProcessCommandRequired => "启动命令不能为空。";

        public string SourceNameRequired => "来源名称不能为空。";

        public string SourceLocationRequired => "来源地址不能为空。";

        public string HealthTimeoutMustBePositiveInteger => "健康检查超时必须是正整数。";

        public string InvalidProjectPathPrefix => "项目路径无效：";

        public string ProjectDirectoryDoesNotExistPrefix => "项目目录不存在：";

        public string InvalidArgumentListPrefix => "参数列表无效：";

        public string InvalidWorkingDirectoryPrefix => "工作目录无效：";

        public string NoMcpServersConfigured => "未配置 MCP 服务。";

        public string McpValidationNotRun => "尚未执行 MCP 体检。";

        public string McpValidationSelectionChanged => "当前选择已变化，请重新执行 MCP 体检。";

        public string AppNotReadyForConfirmation => "当前界面尚未准备好确认对话框。";

        public string AppNotReadyForNotice => "当前界面尚未准备好提示对话框。";

        public string RuntimeScheduledUpdateDisabled => "运行期定时更新：关闭";

        public string NoNeedToRunSourcePolicy => "当前没有需要执行的来源策略。";

        public string OverlayPreviewNotSupported => "仅已登记的 Overlay Skill 支持来源合并预览。";

        public string OverlayPreviewReady => "可预览来源与本地 Overlay 的文件级差异。";

        public string OverlayMergePreviewGenerated => "Overlay 合并预览已生成。";

        public string OverlayMergePreviewRequired => "请先生成 Overlay 合并预览。";

        public string McpValidationScopeNotLoaded => "体检作用域：未加载";

        public string McpValidationCompleted => "MCP 体检已完成。";

        public string McpServiceNotReady => "MCP 服务尚未就绪。";

        public string WorkspaceServiceNotReady => "工作区服务尚未就绪。";

        public string McpValidationFailedTitle => "MCP 体检失败";

        public string ManagedMcpHealthAlertTitle => "托管 MCP 健康检查异常";

                public string ManagedMcpSupervisorSuspendedTitle => "托管 MCP 已被监督暂停";

        public string ManagedMcpRecoveredTitle => "托管 MCP 已恢复健康";

        public string ManagedMcpSupervisorSuspendedSummary(string name) => name + " 达到监督器重启上限，已暂停自动拉起。";

        public string ManagedMcpRecoveredSummary(string name) => name + " 已从异常状态恢复为健康运行。";

        public string OperationSuccessPrefix => "成功：";

        public string OperationFailurePrefix => "失败：";

        public string ProcessDisabled => "已禁用";

        public string ProcessRunning => "运行中";

        public string ProcessStopped => "已停止";

        public string SelectProjectToDelete => "请先选择要删除的项目。";

        public string SelectManagedProcessToDelete => "请先选择要删除的托管进程。";

        public string SelectSkillSourceToDelete => "请先选择要删除的 Skills 来源。";

        public string SelectSkillsSourceFirst => "请先选择一个 Skills 来源。";

        public string SelectSkillsSourceToScan => "请先选择要扫描的 Skills 来源。";

        public string SkillInstallRecordRequired => "请先保存该 Skill 的安装登记。";

        public string NoSkillBackupAvailable => "当前 Skill 没有可用备份。";

        public string ConfigurationPackageExportPathRequired => "请先选择配置包导出路径。";

        public string ConfigurationPackageImportPathRequired => "请先选择要导入的配置包。";

        public string ConfigurationPackagePreviewFailed => "配置包预检失败。";

        public string BackgroundMaintainMcpFailed => "后台维护 MCP 失败。";

        public string BackgroundRefreshFailed => "后台刷新失败。";

        public string BackgroundMaintenanceFailed => "后台维护失败。";

        public string SkillScheduledPolicyExecuted => "来源定时策略已执行。";

        public string NoSourceDifferences => "当前没有需要处理的来源差异。";

        public string SelectExternalMcpFirst => "请先选择至少一个外部 MCP。";

        public string ScopeGlobal => "全局级";

        public string ScopeProject => "项目级";

        public string RegisteredProjectPathLabel => "当前登记路径：";

        public string FormProjectPathLabel => "当前表单路径：";

        public string CheckedPathLabel => "检查路径：";

        public string CurrentProfileLabel => "当前 Profile：";

        public string EffectiveOutputRootLabel => "有效输出根目录：";

        public string NextStepLabel => "下一步：";

        public string SaveProjectFirstStep => "请先点击“新增或更新项目”，保存目录变更后再继续。";

        public string ProjectPathMismatchBlocked => "项目路径与已登记路径不一致，已阻止继续执行。";

        public string ProjectPathMismatchInlineWarning => "表单目录与当前登记目录不一致；接管和项目重扫前请先保存项目。";

        public string WorkspaceBindingNotSelected => "未选择项目，无法检查项目接管入口。";

        public string WorkspaceBindingLegacyMode => "旧式直连";

        public string WorkspaceBindingEffectiveMode => "四层有效输出";

        public string WorkspaceBindingMixedMode => "混合 / 异常";

        public string WorkspaceBindingUnmanagedMode => "未纳管";

        public string WorkspaceBindingMissingTarget => "未发现";

        public string WorkspaceBindingDirectDirectory => "普通目录（未纳管）";

        public string WorkspaceBindingLegacyWarning => "当前项目仍指向旧式直连 skills/<profile>，全局层不会单独出现在项目目录里。请重新应用项目 Profile。";

        public string WorkspaceBindingMixedWarning => "项目 skill 入口目标不一致，建议重新应用项目 Profile。";

        public string WorkspaceBindingUnmanagedWarning => "未检测到完整项目 skill 入口，建议重新应用项目 Profile。";

        public string NoGlobalReimportableResources => "未发现可重新导入的全局资源。";

        public string NoProjectReimportableResources => "未发现可重新导入的项目资源。";

        public string OnboardingPending => "待完成首次接管";

        public string OnboardingProjectNotSelected => "未选择项目";

        public string GlobalOnboardingScanCompleted => "全局接管扫描已完成。";

        public string ProjectOnboardingScanCompleted => "项目接管扫描已完成。";

        public string OnboardingCancelled => "已取消接管导入。";

        public string OnboardingDialogNotReady => "当前界面尚未准备好接管向导。";

        public string UnclosedQuoteDetected => "参数中存在未闭合引号。";

        public string DiagnosticsNotLoaded => "诊断信息尚未加载。";

        public string DiagnosticsExportPathRequired => "请先选择诊断包导出路径。";

        public string NoStartupFailureRecorded => "当前没有记录到启动失败。";

        public string NoUnhandledExceptionRecorded => "当前没有记录到未处理异常。";

        public string RiskConsentPending => "未确认";

        public string DetailProjectNameLabel => "项目名称：";

        public string DetailScopeLabel => "作用域：";

        public string DetailProjectPathLabel => "项目路径：";

        public string DetailNameLabel => "名称：";

        public string DetailStatusLabel => "状态：";

        public string DetailCommandLabel => "命令：";

        public string DetailWorkingDirectoryLabel => "工作目录：";

        public string DetailSourceNameLabel => "来源名称：";

        public string DetailSourceTypeLabel => "来源类型：";

        public string DetailAddressLabel => "地址：";

        public string DetailReferenceLabel => "引用：";

        public string DetailManagedProcessCountLabel => "当前托管进程数：";

        public string DetailSkillLabel => "Skill：";

        public string DetailRelativePathLabel => "相对路径：";

        public string DetailInstalledDirectoryLabel => "安装目录：";

        public string DetailSourceLabel => "来源：";

        public string DetailModeLabel => "模式：";

        public string DetailRollbackBackupLabel => "回滚备份：";

        public string DetailBackupPathLabel => "备份路径：";

        public string DetailSkillPathLabel => "技能路径：";

        public string DetailFileCountLabel => "文件数：";

        public string DetailClientConfigsHeader => "客户端配置：";

        public string DetailIssuesHeader => "问题列表：";

        public string DetailExternalMcpHeader => "外部 MCP：";

        public string DetailSuggestionLabel => "建议：";

        public string MergePreviewRemainingFilesHint => "其余文件请在右侧列表中查看。";

        public string ExternalMcpConflictSuffix => " / 存在冲突";

        public string ProjectCount(int count) => $"项目：{count} 个";

        public string InstalledSkillsSummary(int installed, int registered) => $"已安装 Skills：{installed} 个 / 已登记：{registered} 个";

        public string SkillSourcesSummary(int count) => $"已登记来源：{count} 个";

        public string ScriptsSummary(int count) => $"脚本：{count} 个";

        public string McpRuntimeSummary(int managedCount, int runningCount) => $"托管进程：{managedCount} 个 / 运行中：{runningCount} 个";

        public string ManagedProcessOverviewSummary(int managedCount, int runningCount, int stoppedCount, int alertCount) =>
            $"托管进程：{managedCount} 个 / 运行中：{runningCount} / 已停止：{stoppedCount} / 异常：{alertCount}";

        public string TrayRuntimeSummary(int managedCount, int runningCount, int stoppedCount, int alertCount) =>
            $"MCP：总计 {managedCount} / 运行 {runningCount} / 停止 {stoppedCount} / 异常 {alertCount}";

        public string TrayAlertSummary(int recoverableCount, int attentionCount) =>
            $"可自恢复 {recoverableCount} / 需关注 {attentionCount}";

        public string InvalidProjectPath(string message) => InvalidProjectPathPrefix + message;

        public string ProjectDirectoryDoesNotExist(string path) => ProjectDirectoryDoesNotExistPrefix + path;

        public string InvalidArgumentList(string message) => InvalidArgumentListPrefix + message;

        public string InvalidWorkingDirectory(string message) => InvalidWorkingDirectoryPrefix + message;

        public string NextRunAt(DateTimeOffset dueAt) => "下次到期：" + dueAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        public string NextRunEveryHours(int intervalHours) => $"下次到期：按每 {intervalHours} 小时策略运行";

        public string SkillMergeSummary(string sourceDisplayName, string sourceReference, int fileCount) =>
            $"来源：{sourceDisplayName} / 引用：{sourceReference} / 待处理文件：{fileCount}";

        public string SkillMergePreviewLine(string relativePath, string status, string suggestedDecision) =>
            $"{relativePath} / {status} / {DetailSuggestionLabel}{suggestedDecision}";

        public string McpValidationSummary(int clientCount, int issueCount, int errorCount, int warningCount, int infoCount, int externalCount) =>
            $"客户端：{clientCount} / 问题：{issueCount} / 错误：{errorCount} / 警告：{warningCount} / 信息：{infoCount} / 外部 MCP：{externalCount}";

        public string McpValidationScope(WorkspaceScope scope, string profile, string? projectPath) =>
            scope == WorkspaceScope.Project
                ? $"体检作用域：项目级 / {WorkspaceProfiles.ToDisplayName(profile)} / {projectPath ?? "未选择项目"}"
                : $"体检作用域：全局级 / {WorkspaceProfiles.ToDisplayName(profile)}";

        public string ExternalMcpDisplay(string name, bool hasConflict) => name + (hasConflict ? ExternalMcpConflictSuffix : string.Empty);

        public string ManagedMcpHealthAlertSummary(string processName) => $"{processName} 最近一次健康检查异常。";

        public string ManagedMcpRestartAlertSummary(string processName, int sessionRestartCount) =>
            $"{processName} 在当前会话内已自恢复 {sessionRestartCount} 次。";

        public string ServersSummary(IReadOnlyList<string> servers) =>
            servers.Count == 0
                ? NoMcpServersConfigured
                : $"服务：{servers.Count} 个 / {string.Join("、", servers)}";

        public string OperationSummary(bool success, string message) => (success ? OperationSuccessPrefix : OperationFailurePrefix) + message;

        public string ActiveScope(WorkspaceScope scope) => scope == WorkspaceScope.Project ? ScopeProject : ScopeGlobal;

        public string RiskConsentStatus(bool accepted, DateTimeOffset? acceptedAt)
        {
            if (!accepted)
            {
                return RiskConsentPending;
            }

            return "已确认 / " + (acceptedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "时间未知");
        }

        public string DiagnosticsSummary(string diagnosticsRoot, DateTimeOffset? lastExportedAt, string? lastExportPath)
        {
            var lines = new List<string>
            {
                "诊断目录：" + diagnosticsRoot
            };

            if (lastExportedAt.HasValue)
            {
                lines.Add("最近导出：" + lastExportedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }

            if (!string.IsNullOrWhiteSpace(lastExportPath))
            {
                lines.Add("导出路径：" + lastExportPath);
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string DiagnosticEvent(DateTimeOffset? eventAt, string summary, string details)
        {
            var lines = new List<string>();
            if (eventAt.HasValue)
            {
                lines.Add(eventAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }

            lines.Add(summary);
            if (!string.IsNullOrWhiteSpace(details))
            {
                lines.Add(details);
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string ProcessSummary(string name, bool enabled, bool running, int? processId, string? healthStatus)
        {
            var status = !enabled
                ? ProcessDisabled
                : running
                    ? ProcessRunning
                    : ProcessStopped;

            var pidPart = running && processId.HasValue ? $" / PID {processId.Value}" : string.Empty;
            var healthPart = string.IsNullOrWhiteSpace(healthStatus) ? string.Empty : $" / {healthStatus}";
            return name + " / " + status + pidPart + healthPart;
        }

        public string RescanScope(WorkspaceScope scope) => scope == WorkspaceScope.Project ? ScopeProject : ScopeGlobal;

        public string OnboardingCompleted(DateTimeOffset? completedAt)
        {
            return completedAt.HasValue
                ? "已完成首次接管 / " + completedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : "已完成首次接管";
        }

        public string GlobalOnboardingDialogMessage(bool forceRescan)
        {
            return forceRescan
                ? "重新扫描现有全局资源并决定导入去向。"
                : "首次全局接管前，请先确认现有资源的去向。";
        }

        public string ProjectOnboardingDialogMessage(bool forceRescan)
        {
            return forceRescan
                ? "重新扫描当前项目的现有资源并决定导入去向。"
                : "首次项目接管前，请先确认现有资源的去向。";
        }
    }
}
