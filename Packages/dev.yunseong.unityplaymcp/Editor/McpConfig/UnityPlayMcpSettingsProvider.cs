using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityPlayMcp.Affordances.Live;
using UnityEditor;
using UnityEngine;

namespace UnityPlayMcp.McpConfig.Editor
{
    /// <summary>
    /// <c>Edit &gt; Project Settings &gt; Unity Play MCP</c>. agent 설정 파일에 MCP server 를 넣고 뺀다.
    /// 설정 경로는 <see cref="McpConfigScope"/>, `pulse` 간격은 <see cref="PulseIntervalPreference"/> 로 고른다.
    /// </summary>
    internal static class UnityPlayMcpSettingsProvider
    {
        private const string ServerName = "unity-play";
        private const string McpServerVersionFileFromPackageRoot = "Editor/McpConfig/mcp-server-version.txt";

        private const string ScopeNotice =
            "Switching the scope does not move or delete anything that is already written. " +
            "Each scope keeps its own file, so an entry added under the other scope stays there " +
            "until you switch back and select Remove.";

        private const string CodexProjectScopeNotice =
            "Codex reads $CODEX_HOME/config.toml, and CODEX_HOME defaults to ~/.codex. The project-scope " +
            "Codex file applies only when you start Codex with CODEX_HOME set to <Unity project>/.codex.";

        private const string ReadingIntervalNotice =
            "While the agent is watching the scene in Play Mode, the watched members are read this often. " +
            "A value shorter than the default catches a value that rises and falls again inside one second, " +
            "which a 1 second interval never sees; the game pays for it, because every reading walks the scene " +
            "while it runs. A longer value costs the game less and hides more. The interval applies the next " +
            "time watching starts, not to a run already in progress.";

        private static McpServerEntry _serverEntry;
        private static IReadOnlyList<AgentRow> _rows;
        private static string _projectRoot;
        private static McpConfigRoots _roots;
        private static McpConfigScope _scope;
        private static float _readingInterval;
        private static string _rootsError;
        private static string _serverError;

        [SettingsProvider]
        internal static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Unity Play MCP", SettingsScope.Project)
            {
                label = "Unity Play MCP",
                keywords = new HashSet<string> { "MCP", "agent", "Claude", "Cursor", "Codex", "VS Code", "scope", "interval" },
                activateHandler = (searchContext, rootElement) => Reload(),
                guiHandler = searchContext => Draw(),
            };
        }

        /// <remarks>
        /// project root 는 여기서 한 번만 구해 server 탐색과 설정 파일 경로 계산에 함께 넘긴다.
        /// </remarks>
        private static void Reload()
        {
            _projectRoot = Directory.GetParent(Application.dataPath)?.FullName;

            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var packageRoot = McpServerLocator.PackageRoot();

            _serverEntry = null;
            _serverError = null;

            // local build 는 package metadata 없이도 쓸 수 있다. version file 은 npx 가 필요할 때만 읽는다.
            _serverEntry = McpServerLocator.Resolve(packageRoot, _projectRoot, null, File.Exists);

            if (_serverEntry == null && string.IsNullOrEmpty(packageRoot))
            {
                _serverError = "Could not locate the Unity Play MCP package directory, so the compatible MCP server version is unknown.";
            }
            else if (_serverEntry == null)
            {
                var versionFile = Path.Combine(packageRoot, McpServerVersionFileFromPackageRoot);

                if (!File.Exists(versionFile))
                {
                    _serverError = "The MCP server version file is missing: " + versionFile;
                }
                else
                {
                    try
                    {
                        var mcpServerVersion = File.ReadAllText(versionFile).Trim();

                        if (mcpServerVersion.Length == 0)
                        {
                            _serverError = "The MCP server version file is empty: " + versionFile;
                        }
                        else
                        {
                            _serverEntry = McpServerLocator.Resolve(packageRoot, _projectRoot, mcpServerVersion, File.Exists);
                        }
                    }
                    catch (Exception exception)
                    {
                        _serverError = "Could not read the MCP server version file: " + exception.Message;
                    }
                }
            }

            // agent 행을 그리지 못해도 간격은 보여야 하므로 아래 early return 앞에서 읽는다.
            _readingInterval = string.IsNullOrEmpty(_projectRoot)
                ? PulseIntervalPreference.Default
                : PulseIntervalPreference.Read(_projectRoot);

            // 경로가 비면 매 frame 예외가 나거나 상대경로로 엉뚱한 곳에 파일을 만들므로 여기서 멈춘다.
            if (string.IsNullOrEmpty(_projectRoot) || string.IsNullOrEmpty(homeDirectory))
            {
                _rootsError =
                    "Could not locate the Unity project directory or the home directory, so this page does " +
                    "not know where the agent configuration files are.";
                _roots = null;
                _rows = new List<AgentRow>();
                return;
            }

            _rootsError = null;
            _roots = new McpConfigRoots(
                _projectRoot,
                homeDirectory,
                HostPlatform(),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            _scope = McpConfigScopePreference.Read(_projectRoot);

            BuildRows();
        }

        /// <summary>고른 scope 의 catalog 로 화면의 행을 다시 만든다.</summary>
        /// <remarks>
        /// scope 를 바꾸면 행마다 보는 파일이 달라지므로 status 만 다시 읽으면 안 된다.
        /// </remarks>
        private static void BuildRows()
        {
            var rows = new List<AgentRow>();

            foreach (var agent in McpAgent.Catalog(_scope, _roots))
            {
                rows.Add(new AgentRow(agent));
            }

            _rows = rows;
            ReadStatus();
        }

        /// <remarks>
        /// Unity editor 는 세 운영체제에서만 돌므로 그 밖의 값은 Linux 로 본다.
        /// </remarks>
        private static McpHostPlatform HostPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return McpHostPlatform.Windows;
                case RuntimePlatform.OSXEditor:
                    return McpHostPlatform.MacOs;
                default:
                    return McpHostPlatform.Linux;
            }
        }

        private static void ReadStatus()
        {
            foreach (var row in _rows)
            {
                try
                {
                    row.Configured = McpAgentConfigurator.IsConfigured(row.Agent, ServerName);
                    row.Error = null;
                }
                catch (Exception exception)
                {
                    // 읽을 수 없는 파일은 쓸 수도 없으므로 두 버튼을 모두 막는다.
                    row.Configured = false;
                    row.Error = exception.Message;
                }
            }
        }

        private static void Draw()
        {
            if (_rows == null)
            {
                Reload();
            }

            EditorGUILayout.LabelField("MCP server", EditorStyles.boldLabel);

            if (_serverError != null)
            {
                EditorGUILayout.HelpBox(_serverError, MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Command",
                    _serverEntry.Command + " " + string.Join(" ", _serverEntry.Arguments),
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Selection", _serverEntry.Reason, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Agents", EditorStyles.boldLabel);

            if (_rootsError != null)
            {
                EditorGUILayout.HelpBox(_rootsError, MessageType.Error);
            }
            else
            {
                DrawScopePopup();

                foreach (var row in _rows)
                {
                    DrawRow(row);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live readings", EditorStyles.boldLabel);
            DrawReadingInterval();

            EditorGUILayout.Space();

            // root 경로를 구하지 못했을 때도 다시 시도할 수 있도록 이 버튼은 항상 그린다.
            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
            {
                Reload();
            }
        }

        /// <remarks>
        /// 범위 밖의 값을 잘라내므로 <c>DelayedFloatField</c> 를 쓴다. <c>FloatField</c> 는 입력 중인 "0" 을
        /// 바로 최솟값으로 잘라 "0.25" 를 입력할 수 없다.
        /// </remarks>
        private static void DrawReadingInterval()
        {
            if (string.IsNullOrEmpty(_projectRoot))
            {
                EditorGUILayout.HelpBox(
                    "Could not locate the Unity project directory, so this page cannot remember a reading interval.",
                    MessageType.Error);
                return;
            }

            var typed = EditorGUILayout.DelayedFloatField("Reading interval (s)", _readingInterval);

            if (typed != _readingInterval)
            {
                // 입력란이 잘리기 전 값을 들고 있지 않도록 저장된 값으로 되돌린다.
                _readingInterval = PulseIntervalPreference.Write(_projectRoot, typed);
            }

            EditorGUILayout.LabelField(
                " ",
                "Default " + PulseIntervalPreference.Default.ToString(CultureInfo.InvariantCulture) + " s, " +
                "between " + PulseIntervalPreference.Minimum.ToString(CultureInfo.InvariantCulture) + " s and " +
                PulseIntervalPreference.Maximum.ToString(CultureInfo.InvariantCulture) + " s.",
                EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(ReadingIntervalNotice, MessageType.Info);
        }

        /// <remarks>
        /// scope 는 파일을 바꾸지 않고 볼 파일만 바꾸므로 고르는 즉시 저장하고 행을 다시 만든다.
        /// </remarks>
        private static void DrawScopePopup()
        {
            var chosen = (McpConfigScope)EditorGUILayout.EnumPopup("Configuration scope", _scope);

            if (chosen != _scope)
            {
                _scope = chosen;
                McpConfigScopePreference.Write(_projectRoot, _scope);
                BuildRows();
            }

            EditorGUILayout.HelpBox(ScopeNotice, MessageType.Info);

            if (_scope == McpConfigScope.Project)
            {
                EditorGUILayout.HelpBox(CodexProjectScopeNotice, MessageType.Info);
            }
        }

        private static void DrawRow(AgentRow row)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(row.Agent.DisplayName, GUILayout.Width(110f));
            EditorGUILayout.LabelField(Status(row), GUILayout.Width(110f));

            using (new EditorGUI.DisabledScope(_serverEntry == null || row.Error != null))
            {
                if (GUILayout.Button("Add", GUILayout.Width(70f)))
                {
                    Apply(row, add: true);
                }
            }

            using (new EditorGUI.DisabledScope(row.Error != null || !row.Configured))
            {
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    Apply(row, add: false);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(" ", row.Agent.ConfigPath, EditorStyles.miniLabel);

            if (row.Error != null)
            {
                EditorGUILayout.HelpBox(row.Agent.ConfigPath + "\n" + row.Error, MessageType.Error);
            }
        }

        private static string Status(AgentRow row)
        {
            if (row.Error != null)
            {
                return "Unreadable";
            }

            return row.Configured ? "Configured" : "Not configured";
        }

        private static void Apply(AgentRow row, bool add)
        {
            try
            {
                if (add)
                {
                    McpAgentConfigurator.Add(row.Agent, ServerName, _serverEntry);
                }
                else
                {
                    McpAgentConfigurator.Remove(row.Agent, ServerName);
                }
            }
            catch (Exception exception)
            {
                // 변환이 실패하면 파일을 쓰기 전에 여기로 오므로 기존 설정은 그대로 남는다.
                row.Error = exception.Message;
                Debug.LogException(exception);
                return;
            }

            ReadStatus();
        }

        private sealed class AgentRow
        {
            internal AgentRow(McpAgent agent)
            {
                Agent = agent;
            }

            internal McpAgent Agent { get; }

            internal bool Configured { get; set; }

            internal string Error { get; set; }
        }
    }
}
