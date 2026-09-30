using System;
using System.Collections.Generic;
using System.Text;
using UnityPlayMcp.Affordances.Live;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPlayMcp.Affordances.Scan
{
    /// <summary>
    /// 코드 분석 `evidence` 와 씬의 실제 객체, `wiring` 을 잇는다.
    /// </summary>
    /// <remarks>
    /// `evidence` 는 스키마를 분석기와 읽는 쪽이 정하므로 파싱하지 않고 그대로 통과시킨다.
    /// </remarks>
    public static class SceneEvidenceScan
    {
        private const int MaxObjects = 5000;
        private const int MaxComponentsPerObject = 128;
        private const int MaxCallsPerComponent = 64;

        /// <summary>
        /// 라벨을 찾아 내려가는 자식 깊이.
        /// </summary>
        /// <remarks>
        /// 캡션은 보통 자식 한두 단계 아래에 있다. 서브트리 전체를 보면 캔버스가 화면의 모든 텍스트를 가져간다.
        /// </remarks>
        private const int MaxLabelDepth = 3;

        /// <summary>로드된 모든 씬을 읽어 리포트에 더한다.</summary>
        public static int CaptureLoaded()
        {
            var captured = 0;

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                if (Capture(SceneManager.GetSceneAt(index)))
                {
                    captured++;
                }
            }

            return captured;
        }

        /// <summary>씬 하나를 읽어 리포트의 그 씬 항목을 교체한다.</summary>
        public static bool Capture(Scene scene)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            var gaps = new List<string>();

            if (!scene.isLoaded)
            {
                AffordanceReport.Merge(scene.name, string.Empty, new List<string> { "scene-not-loaded" });
                return false;
            }

            var text = new StringBuilder(4096);
            var objects = 0;
            var first = true;

            var roots = scene.GetRootGameObjects();

            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];

                // 지금 꺼져 있는 메뉴도 게임이 보여 줄 수 있으므로 비활성 객체도 포함한다.
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (objects >= MaxObjects)
                    {
                        gaps.Add("object-limit");
                        break;
                    }

                    if (Describe(text, transform.gameObject, scene.name, rootIndex, gaps, ref first))
                    {
                        objects++;
                    }
                }
            }

            AffordanceReport.Merge(scene.name, text.ToString(), gaps);
            return true;
        }

        /// <summary>
        /// DontDestroyOnLoad 씬의 객체들을 읽는다.
        /// </summary>
        /// <remarks>
        /// 세이브 컨트롤러, 싱글턴 같은 객체가 여기 있으며 빌드 설정 순회로는 닿지 않는다.
        /// 특정 씬에 속하지 않으므로 씬 항목과 분리해 <c>persistentObjects</c> 에 쓴다.
        /// 게임이 돌기 전에는 이 씬이 없으며, 리포트는 그 사실을 gap 으로 남긴다.
        /// </remarks>
        public static bool CapturePersistent(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            var gaps = new List<string>();
            var text = new StringBuilder(1024);
            var first = true;
            var roots = scene.GetRootGameObjects();

            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var root = roots[rootIndex];

                // 순회의 `carrier` 같은 도구 객체는 게임의 것이 아니므로 뺀다.
                if (root == null || root.hideFlags != HideFlags.None)
                {
                    continue;
                }

                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Describe(text, transform.gameObject, scene.name, rootIndex, gaps, ref first);
                }
            }

            AffordanceReport.Persistent(text.ToString(), gaps);
            return true;
        }

        /// <summary>객체 하나를 쓴다. 쓸 내용이 없으면 false.</summary>
        private static bool Describe(
            StringBuilder text,
            GameObject subject,
            string scene,
            int rootIndex,
            List<string> gaps,
            ref bool first)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                gaps.Add("components-unreadable:" + subject.name);
                return false;
            }

            var body = new StringBuilder(256);
            var wrote = false;
            var limit = Math.Min(components.Length, MaxComponentsPerObject);

            if (components.Length > MaxComponentsPerObject)
            {
                gaps.Add("component-limit:" + subject.name);
            }

            for (var index = 0; index < limit; index++)
            {
                var component = components[index];

                if (component == null)
                {
                    // 타입이 사라진 스크립트. 건너뛰면 망가진 객체가 멀쩡해 보이므로 gap 으로 보고한다.
                    gaps.Add("missing-script:" + subject.name);
                    continue;
                }

                if (Describe(body, component, wrote))
                {
                    wrote = true;
                }
            }

            // 무언가를 그리는 객체는 쓸 컴포넌트가 없어도 쓴다. `pulse` 와 같은 규칙이어야 한다(`Worth` 의 remarks).
            // 텍스트와 이미지는 컴포넌트로 쓰지 않으므로 이런 객체는 `components` 가 비어 있다.
            if (!wrote && !Drawn.Any(subject))
            {
                return false;
            }

            if (!first)
            {
                text.Append(',');
            }

            first = false;

            var path = ScenePath.Of(subject.transform);

            text.Append('{');
            Json.Property(text, "path", path);
            text.Append(',');

            // 같은 경로를 공유하는 객체들을 `selector` 로 구분한다.
            Json.Property(text, "selector", ScenePath.SelectorOf(subject.transform, rootIndex));
            text.Append(',');
            Json.Property(text, "scene", scene);
            text.Append(',');
            Json.Property(text, "active", subject.activeInHierarchy);

            var seen = new Showing();
            Gather(subject.transform, 0, seen, false);

            var captions = seen.Only(Caption);
            var pictures = seen.Only(Picture);

            if (seen.Count(Caption) > 1)
            {
                gaps.Add("several-labels:" + path);
            }
            else if (captions != null)
            {
                text.Append(',');
                Json.Property(text, "label", captions.Value);
                text.Append(',');
                Json.Property(text, "labelFrom", captions.From);
            }
            else if (seen.Count(Picture) > 1)
            {
                gaps.Add("several-sprites:" + path);
            }
            else if (pictures != null)
            {
                text.Append(',');
                Json.Property(text, "sprite", pictures.Value);
                text.Append(',');
                Json.Property(text, "spriteFrom", pictures.From);
            }

            if (seen.All.Count > 0)
            {
                text.Append(",\"visuals\":[");

                for (var index = 0; index < seen.All.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append(',');
                    }

                    var visual = seen.All[index];

                    text.Append('{');
                    Json.Property(text, "role", visual.Role);
                    text.Append(',');
                    Json.Property(text, "value", visual.Value);
                    text.Append(',');
                    Json.Property(text, "from", visual.From);
                    text.Append(',');
                    Json.Property(text, "type", visual.Type);
                    text.Append('}');
                }

                text.Append(']');
            }

            text.Append(",\"components\":[").Append(body).Append("]}");
            return true;
        }

        /// <summary>플레이어가 누를 수 있는 것 위의 텍스트.</summary>
        private const string Caption = "control-caption";

        /// <summary>컨트롤 이름이 아니라 표시값인 텍스트.</summary>
        private const string Observed = "observed-text";

        /// <summary>무언가 위에 그려진 그림.</summary>
        private const string Picture = "sprite";

        /// <summary>
        /// 객체가 보여 주는 텍스트나 그림 하나.
        /// </summary>
        /// <remarks>
        /// 객체 이름은 화면에 없는 개발자용 이름이므로 플레이어가 보는 텍스트를, 없으면 스프라이트 이름을 따로 쓴다.
        /// 텍스트와 이미지는 컴포넌트로 쓰지 않는다. 쓰면 작용 대상인 컴포넌트가 파묻힌다.
        /// 서로 다른 캡션이 여럿이면 추측하지 않고 라벨을 비운다. 같은 텍스트 둘(캡션과 그림자)은 하나로 본다.
        /// 스캔 시점의 관측값이지 규칙이 아니다.
        /// </remarks>
        private sealed class Visual
        {
            internal string Role;
            internal string Value;
            internal string From;
            internal string Type;

            /// <summary>누를 수 있는 컨트롤 위에 있어 컨트롤 이름일 수 있다.</summary>
            internal bool OnControl;
        }

        private sealed class Showing
        {
            internal readonly List<Visual> All = new List<Visual>();

            internal void Add(string role, string value, string from, string type, bool onControl)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                foreach (var seen in All)
                {
                    // 캡션과 그 그림자처럼 같은 값은 하나로 본다.
                    if (seen.Role == role && seen.Value == value)
                    {
                        return;
                    }
                }

                All.Add(new Visual
                {
                    Role = role, Value = value, From = from, Type = type, OnControl = onControl
                });
            }

            /// <summary>컨트롤 위에 있는 해당 역할 항목의 수.</summary>
            internal int Count(string role)
            {
                var found = 0;

                foreach (var visual in All)
                {
                    if (visual.Role == role && visual.OnControl)
                    {
                        found++;
                    }
                }

                return found;
            }

            internal Visual Only(string role)
            {
                Visual found = null;

                foreach (var visual in All)
                {
                    if (visual.Role != role || !visual.OnControl)
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        return null;
                    }

                    found = visual;
                }

                return found;
            }
        }

        private static void Gather(Transform at, int depth, Showing seen, bool pressable)
        {
            Component[] components;

            try
            {
                components = at.GetComponents<Component>();
            }
            catch (Exception)
            {
                return;
            }

            var path = ScenePath.Of(at);

            // 누를 수 있는 객체 아래의 모든 것은 그 컨트롤 위에 있는 것으로 본다.
            pressable = pressable || Pressable(components);

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType().FullName;

                seen.Add(pressable ? Caption : Observed, TextOf(component), path, type, pressable);
                seen.Add(Picture, SpriteOf(component), path, type, pressable);
            }

            if (depth >= MaxLabelDepth)
            {
                return;
            }

            for (var index = 0; index < at.childCount; index++)
            {
                Gather(at.GetChild(index), depth + 1, seen, pressable);
            }
        }

        /// <summary>
        /// 플레이어가 누를 수 있는 객체인지. 캡션과 표시값을 가르는 기준이다.
        /// </summary>
        /// <remarks>
        /// 텍스트 모양이 아니라 객체로 판단한다. <c>Selectable</c> 아래 텍스트는 캡션이고 그 밖은 표시값이다.
        /// 이 어셈블리는 uGUI 를 참조하지 않으므로 타입 이름으로 비교한다.
        /// </remarks>
        private static bool Pressable(Component[] components)
        {
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                for (var type = component.GetType(); type != null; type = type.BaseType)
                {
                    if (type.FullName == "UnityEngine.UI.Selectable")
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 텍스트 컴포넌트의 현재 문자열. 텍스트 컴포넌트가 아니면 null.
        /// </summary>
        /// <remarks>
        /// uGUI 와 TextMeshPro 는 없을 수 있는 패키지라 참조하지 않고 기반 타입 이름과 리플렉션으로 읽는다.
        /// 기반 타입은 난독화되지 않고 <c>TextMeshProUGUI</c> 와 <c>TextMeshPro</c> 를 함께 덮는다.
        /// </remarks>
        internal static string TextOf(Component component)
        {
            if (component == null)
            {
                return null;
            }

            var type = component.GetType();

            if (!IsLabel(type))
            {
                return null;
            }

            try
            {
                var property = type.GetProperty("text");
                var value = property == null ? null : property.GetValue(component, null) as string;

                return value == null ? null : value.Trim();
            }
            catch (Exception)
            {
                // 프로퍼티가 던져도 객체 서술은 계속한다.
                return null;
            }
        }

        private static bool IsLabel(Type type)
        {
            return Derives(type, "UnityEngine.UI.Text") || Derives(type, "TMPro.TMP_Text");
        }

        /// <summary>
        /// 컴포넌트가 그리는 스프라이트 이름. Unity 기본 스프라이트면 null.
        /// </summary>
        /// <remarks>
        /// <c>UISprite</c> 같은 기본 스프라이트 이름은 게임에 대해 아무것도 말하지 않으므로 보고하지 않는다.
        /// </remarks>
        internal static string SpriteOf(Component component)
        {
            if (component == null)
            {
                return null;
            }

            var type = component.GetType();

            if (!Derives(type, "UnityEngine.UI.Image") && !Derives(type, "UnityEngine.SpriteRenderer"))
            {
                return null;
            }

            try
            {
                var property = type.GetProperty("sprite");
                var drawn = property == null ? null : property.GetValue(component, null) as UnityEngine.Object;
                var name = drawn == null ? null : drawn.name;

                return Array.IndexOf(UnitysOwn, name) < 0 ? name : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static readonly string[] UnitysOwn =
        {
            "UISprite", "Background", "Knob", "Checkmark", "DropdownArrow", "InputFieldBackground",
            "UIMask"
        };

        private static bool Derives(Type type, string name)
        {
            for (var at = type; at != null; at = at.BaseType)
            {
                if (at.FullName == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>컴포넌트 하나를 쓴다. 쓸 내용이 없으면 false.</summary>
        private static bool Describe(StringBuilder text, Component component, bool needsComma)
        {
            var evidence = AffordanceCatalog.For(component.GetType());
            var calls = new List<PersistentCall>();

            try
            {
                PersistentCallReader.Read(component, calls);
            }
            catch (Exception)
            {
                calls.Clear();
            }

            // `evidence` 도 `wiring` 도 없는 컴포넌트는 쓰지 않는다. 쓰면 작용할 수 있는 컴포넌트가 파묻힌다.
            if (string.IsNullOrEmpty(evidence) && calls.Count == 0)
            {
                return false;
            }

            var refs = new List<Reference>();

            // 비용이 크므로 쓸 컴포넌트에 대해서만 참조를 읽는다.
            try
            {
                SerializedReferences.Read(component, refs);
            }
            catch (Exception)
            {
                refs.Clear();
            }

            if (needsComma)
            {
                text.Append(',');
            }

            var type = component.GetType().FullName;

            // `evidence` 는 인스턴스마다 같으므로 객체가 아니라 `types` 표에 타입당 한 번 쓴다.
            Remember(type, evidence);

            text.Append('{');
            Json.Property(text, "type", type);

            text.Append(",\"calls\":[");
            var limit = Math.Min(calls.Count, MaxCallsPerComponent);

            for (var index = 0; index < limit; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                var call = calls[index];

                // `wiring` 의 대상은 이 컴포넌트와 다른 타입이므로 이 컴포넌트에 `evidence` 가 있어도 기록한다.
                AffordanceReport.Wired(call.TargetType);

                text.Append('{');
                Json.Property(text, "event", call.Event);
                text.Append(',');
                Json.Property(text, "targetType", call.TargetType);
                text.Append(',');
                Json.Property(text, "targetPath", call.TargetPath);
                text.Append(',');
                Json.Property(text, "method", call.Method);
                text.Append('}');
            }

            text.Append("],\"refs\":[");

            for (var index = 0; index < refs.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(',');
                }

                var reference = refs[index];
                text.Append('{');
                Json.Property(text, "field", reference.Field);
                text.Append(',');
                Json.Property(text, "type", reference.Type);
                text.Append(',');
                Json.Property(text, "name", reference.Name);
                text.Append(',');
                Json.Property(text, "id", reference.Id);
                text.Append(',');
                Json.Property(text, "path", reference.Path);
                text.Append(',');

                // 테스트가 찾아갈 수 있는 것은 씬 객체뿐이므로 프리팹 애셋인지 명시한다.
                Json.Property(text, "asset", reference.Asset);
                text.Append(",\"carries\":[");

                if (reference.Carries != null)
                {
                    for (var carried = 0; carried < reference.Carries.Count; carried++)
                    {
                        if (carried > 0)
                        {
                            text.Append(',');
                        }

                        Json.String(text, reference.Carries[carried]);
                    }
                }

                text.Append("]}");

                // 씬을 떠나면 소유 필드를 알 수 없으므로 지금 추적해 `createdBy` 를 채운다.
                // 프리팹은 ScriptableObject 를 거쳐 참조되는 일이 많아 몇 단계 더 따라간다.
                if (reference.Asset)
                {
                    try
                    {
                        SerializedReferences.Trace(reference.Held, type, reference.Field);
                    }
                    catch (Exception)
                    {
                        // 추적에 실패해도 씬 서술은 계속한다.
                    }
                }
            }

            text.Append("]}");
            return true;
        }

        /// <summary>타입을 처음 만났을 때 그 `evidence` 를 표에 넣는다.</summary>
        /// <remarks>
        /// `evidence` 는 이미 JSON 배열이므로 문자열로 감싸지 않고 그대로 넣는다.
        /// </remarks>
        private static void Remember(string type, string evidence)
        {
            if (string.IsNullOrEmpty(evidence) || AffordanceReport.Knows(type))
            {
                return;
            }

            AffordanceReport.Learn(type, evidence);
        }
    }
}
