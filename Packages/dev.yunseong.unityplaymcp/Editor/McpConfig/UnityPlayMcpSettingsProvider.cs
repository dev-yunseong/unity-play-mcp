using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// <c>Edit &gt; Project Settings &gt; Unity Play MCP</c>. agent 네 곳의 설정 파일에 이 저장소의 MCP server 를
    /// 넣고 뺀다. 어느 자리의 설정을 볼지는 <see cref="McpConfigScope"/> 로 고른다.
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

        private static McpServerEntry _serverEntry;
        private static IReadOnlyList<AgentRow> _rows;
        private static string _projectRoot;
        private static McpConfigRoots _roots;
        private static McpConfigScope _scope;
        private static string _rootsError;
        private static string _serverError;

        [SettingsProvider]
        internal static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Unity Play MCP", SettingsScope.Project)
            {
                label = "Unity Play MCP",
                keywords = new HashSet<string> { "MCP", "agent", "Claude", "Cursor", "Codex", "VS Code", "scope" },
                activateHandler = (searchContext, rootElement) => Reload(),
                guiHandler = searchContext => Draw(),
            };
        }

        /// <remarks>
        /// project 루트는 여기서 한 번만 구해 server 를 찾는 쪽과 설정 파일 자리를 정하는 쪽에 함께 넘긴다.
        /// 두 곳이 각자 계산하면 같은 개념이 두 가지 방식으로 갈라진다.
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

            // 둘 중 하나라도 비면 설정 파일 자리를 못 정한다. 그대로 Path.Combine 에 넘기면 화면이 예외로
            // 매 frame 깨지거나, 홈 디렉터리 없이 상대경로가 만들어져 엉뚱한 자리에 파일을 새로 만든다.
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
        /// scope 를 바꾸면 행마다 보는 파일이 통째로 달라지므로 status 만 다시 읽어서는 안 된다.
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
        /// Unity editor 는 세 운영체제에서만 돈다. 그 밖의 값이 오면 Linux 의 자리를 쓴다. Unity 가 아직 없는
        /// 운영체제를 여기서 미리 나눠 두어도 확인할 방법이 없다.
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
                    // 읽을 수 없는 파일은 쓸 수도 없다. 등록 여부를 모른다고 말하고 두 버튼을 다 막는다.
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

            // 뿌리 경로를 못 구한 상태에서도 이 버튼은 남는다. 경로를 못 구한 이유가 사라졌을 때 화면을
            // 닫았다 여는 것 말고 다시 시도할 방법이 필요하다.
            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
            {
                Reload();
            }
        }

        /// <remarks>
        /// 고른 값은 파일에 손대지 않고 어느 파일을 볼지만 바꾼다. 그래서 고르는 즉시 저장하고 행을 다시 만든다.
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
                // entry 는 넣을 때만 뜻이 있다. 빼는 쪽에서도 만들면 null 값이 args 에 실릴 수 있다.
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
                // 변환이 실패했으면 아무것도 쓰지 않은 채로 여기 온다. 사람이 쓴 설정이 그대로 남는 것이 중요하다.
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
