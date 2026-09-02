using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Artel.McpConfig.Editor
{
    /// <summary>
    /// <c>Edit &gt; Project Settings &gt; Unity Play MCP</c>. agent 네 곳의 설정 파일에 이 저장소의 MCP server 를
    /// 넣고 뺀다.
    /// </summary>
    internal static class UnityPlayMcpSettingsProvider
    {
        private const string ServerName = "unity-play";
        private const string ServerCommand = "node";

        private static string _entryPoint;
        private static IReadOnlyList<AgentRow> _rows;
        private static string _rootsError;

        [SettingsProvider]
        internal static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Unity Play MCP", SettingsScope.Project)
            {
                label = "Unity Play MCP",
                keywords = new HashSet<string> { "MCP", "agent", "Claude", "Cursor", "Codex", "VS Code" },
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
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _entryPoint = McpServerLocator.FindEntryPoint(McpServerLocator.PackageRoot(), projectRoot, File.Exists);

            // 둘 중 하나라도 비면 설정 파일 자리를 못 정한다. 그대로 Path.Combine 에 넘기면 화면이 예외로
            // 매 frame 깨지거나, 홈 디렉터리 없이 상대경로가 만들어져 엉뚱한 자리에 파일을 새로 만든다.
            if (string.IsNullOrEmpty(projectRoot) || string.IsNullOrEmpty(homeDirectory))
            {
                _rootsError =
                    "Could not locate the Unity project directory or the home directory, so this page does " +
                    "not know where the agent configuration files are.";
                _rows = new List<AgentRow>();
                return;
            }

            _rootsError = null;

            var rows = new List<AgentRow>();

            foreach (var agent in McpAgent.Catalog(projectRoot, homeDirectory))
            {
                rows.Add(new AgentRow(agent));
            }

            _rows = rows;
            ReadStatus();
        }

        private static void ReadStatus()
        {
            foreach (var row in _rows)
            {
                try
                {
                    row.Configured = row.Agent.Format.Contains(McpConfigFileStore.Read(row.Agent.ConfigPath), ServerName);
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

            if (_entryPoint == null)
            {
                EditorGUILayout.HelpBox(
                    "Built MCP server not found. Clone the unity-play-mcp repository, then run " +
                    "`npm install && npm run build` in its mcp/ directory.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Command", ServerCommand + " " + _entryPoint, EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Agents", EditorStyles.boldLabel);

            if (_rootsError != null)
            {
                EditorGUILayout.HelpBox(_rootsError, MessageType.Error);
            }

            foreach (var row in _rows)
            {
                DrawRow(row);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
            {
                Reload();
            }
        }

        private static void DrawRow(AgentRow row)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(row.Agent.DisplayName, GUILayout.Width(110f));
            EditorGUILayout.LabelField(Status(row), GUILayout.Width(110f));

            using (new EditorGUI.DisabledScope(_entryPoint == null || row.Error != null))
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
                var text = McpConfigFileStore.Read(row.Agent.ConfigPath);

                // entry 는 넣을 때만 뜻이 있다. 빼는 쪽에서도 만들면 _entryPoint 가 null 인 채로 args 에 실린다.
                var updated = add
                    ? row.Agent.Format.Add(text, ServerName, new McpServerEntry(ServerCommand, new[] { _entryPoint }))
                    : row.Agent.Format.Remove(text, ServerName);

                McpConfigFileStore.Write(row.Agent.ConfigPath, updated);
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
