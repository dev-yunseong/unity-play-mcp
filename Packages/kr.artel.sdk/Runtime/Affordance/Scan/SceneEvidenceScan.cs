using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 코드가 하는 것으로 밝혀진 것과, 한 씬이 실제로 쥐고 있는 것을 잇는다.
    /// </summary>
    /// <remarks>
    /// 컴파일된 근거는 타입과 메서드를 알고 화면에 대해서는 아무것도 모른다. 씬은 어떤 객체가 존재하는지, 어느 것이 꺼져
    /// 있는지, 어느 버튼이 어느 메서드에 연결됐는지를 알고, 그것들이 무엇을 하는지에 대해서는 아무것도 모른다. 어느 절반도
    /// 홀로 명세가 아니다.
    ///
    /// 근거 문서는 손대지 않고 그대로 통과시킨다. 그 스키마는 그것을 쓴 분석기와 그것을 읽는 에이전트의 것이다. 여기서 다시
    /// 파싱하면 그 둘 모두와 발을 맞춰야 하는 세 번째 의견을 한가운데 두게 된다.
    /// </remarks>
    public static class SceneEvidenceScan
    {
        private const int MaxObjects = 5000;
        private const int MaxComponentsPerObject = 128;
        private const int MaxCallsPerComponent = 64;

        /// <summary>
        /// 객체 아래로 그 라벨을 얼마나 깊이 찾는지.
        /// </summary>
        /// <remarks>
        /// 캡션은 자식 한둘 아래에 앉아 있다. 서브트리 전체를 걸으면 캔버스가 화면의 모든 단어를 제 것이라 주장하게 되고,
        /// 그에 대한 답은 틀린 라벨이 아니라 라벨 없음이다 — 여기서 단어가 여럿이라는 것이 이미 뜻하는 바가 그것이다.
        /// </remarks>
        private const int MaxLabelDepth = 3;

        /// <summary>로드된 모든 씬을 리포트로 읽어 들인다.</summary>
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

        /// <summary>씬 하나를 리포트로 읽어 들이고, 전에 그것에 대해 말한 것을 갈아치운다.</summary>
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

                // 비활성 객체를 일부러 포함한다. 지금 꺼져 있는 메뉴도 게임이 보여 줄 수 있는 것이고, 그것을 빼면 결과가 화면이 아니라
                // 한순간에 대해서만 참이 된다.
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
        /// 게임이 씬 로드를 건너 쥐고 있던 객체들을 읽는다.
        /// </summary>
        /// <remarks>
        /// 그것들은 빌드 설정을 순회해서는 결코 닿지 않는 제 씬에 앉아 있고, 거기가 게임이 잃고 싶지 않은 것을 두는 자리다 —
        /// 세이브 컨트롤러, 싱글턴, 실행의 진행 상황. 리포트의 모든 씬이 예전에는 그렇다고 말하는 공백을 나르고 있었다. 공백은
        /// 아무도 들여다보지 않은 것에 대해 할 말로는 옳지만, 일단 누군가 들여다볼 수 있게 된 뒤에도 계속 할 말로는 틀렸다.
        ///
        /// 씬마다 복사해 넣지 않고 씬들과 떼어 둔다. 이 객체들 중 하나는 어느 화면에도 없으면서 모든 화면에 있고, 그것을 씬
        /// 이름 아래에 쓰면 테스터가 거기서 찾을 수 있는 무엇처럼 보이게 된다.
        ///
        /// 게임이 돌기 전에는 여기 아무것도 없다. 에디터 순회는 씬을 저장된 대로 여는 것이므로 읽을 그런 씬이 아예 없고,
        /// 리포트는 게임에 그런 것이 없는 척하는 대신 그렇다고 말한다.
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

                // 순회 자신의 carrier 도 여기 산다. 그것을 보고하는 것은 게임이 아니라 계기를 보고하는 일이다.
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

        /// <summary>객체 하나를 쓰고, 쓸 값이 있었는지 말한다.</summary>
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
                    // 타입이 더는 존재하지 않는 스크립트. 빠진 컴포넌트는 망가진 객체이고, 그것을 조용히 건너뛰는 스캔은 씬을 멀쩡해
                    // 보이게 만들기 때문에 보고한다.
                    gaps.Add("missing-script:" + subject.name);
                    continue;
                }

                if (Describe(body, component, wrote))
                {
                    wrote = true;
                }
            }

            if (!wrote)
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

            // 한 종류의 적 다섯은 경로를 공유한다. 다섯 중 어느 것인지는 경로가 답할 수 없고 이것이 답할 수 있는 물음이다.
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

        /// <summary>플레이어가 누를 수 있는 것 위의 단어들.</summary>
        private const string Caption = "control-caption";

        /// <summary>그것을 보여 주는 것의 이름이 아니라, 읽으라고 거기 있는 단어들.</summary>
        private const string Observed = "observed-text";

        /// <summary>무언가 위에 그려진 그림.</summary>
        private const string Picture = "sprite";

        /// <summary>
        /// 객체가 무엇을 보여 주는가 — 그 위의 단어들, 그것이 없으면 그 위에 그려진 그림.
        /// </summary>
        /// <remarks>
        /// 객체의 이름은 개발자가 그것을 부른 이름이고, 거기서 쓴 테스트 단계는 테스터에게 화면 어디에도 적혀 있지 않은 것을
        /// 누르라고 시킨다. 샘플 게임에서 버튼 하나는 <c>Button (Legacy)</c> 라 불리는데 그것은 Unity 자신의 자리표시자이고
        /// 아무 말도 하지 않으며, 다른 하나는 이야기를 여는데도 <c>MapSceneButton</c> 이라 불린다 — 답처럼 읽히기 때문에 없는
        /// 것보다 나쁜 이름이다.
        ///
        /// 요점은 단어인데, 그 게임에서는 어떤 버튼에도 단어가 없다: 하나같이 그림이다. 그래서 텍스트가 없을 때 스프라이트의
        /// 이름을 취하고, 애셋의 파일 이름은 화면이 말하는 바가 아니므로 제 필드에 따로 둔다 — 그것이 존재하는 것 중 그에 가장
        /// 가까운 것이고, <c>MapSceneButton</c> 이 무엇인지 결판내기에는 충분했다 (<c>Sprite_Start_Button</c>).
        ///
        /// 둘 다 컴포넌트로 쓰지 않는다. 텍스트와 이미지는 테스트가 작용하는 것이 아니고, 그것들을 리포트에 넣으면 정작 작용
        /// 대상인 몇 개가 그것들이 칠해진 배경 아래 파묻힌다.
        ///
        /// 한 객체 아래 서로 다른 단어가 여럿이면 아무것도 취하지 않는다. 그중 무엇이 라벨인지는 이것이 답할 수 없는 물음이고 —
        /// 버튼은 캡션과 그림자와 개수를 함께 나를 수 있다 — 추측이야말로 답처럼 읽히는 이름이 애초에 만들어지는 방식이다. 같은
        /// 단어 둘은 한 단어이므로, 캡션과 그 그림자는 불일치가 아니다.
        ///
        /// 이 전부는 관측이지 규칙이 아니다: 스캔이 도는 동안 화면이 보여 준 것이고, 게임이 런타임에 쓰는 라벨은 한순간 전에는
        /// 다른 것이었다.
        /// </remarks>
        private sealed class Visual
        {
            internal string Role;
            internal string Value;
            internal string From;
            internal string Type;

            /// <summary>플레이어가 누를 수 있는 것 위에 그려져 있어, 그 컨트롤의 이름일 수 있는 것.</summary>
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
                    // 같은 단어 둘은 한 단어다 — 캡션과 그 그림자는 보여 주는 것 둘이 아니다.
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

            /// <summary>그 컨트롤 위에 그려진 어떤 역할의 것이 몇 개인지. 그것이 컨트롤의 이름일 수 있는 것이다.</summary>
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

            // 내려가는 길에 누를 수 있는 것이 한 번 나오면, 그 아래에 그려진 모든 것은 눌리는 그것 위에 그려진 것이다.
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
        /// 플레이어가 이것을 누를 수 있는지. 그것이 캡션과 표시값을 가른다.
        /// </summary>
        /// <remarks>
        /// 리포트가 계속 틀리게 답하던 물음은 객체의 단어들 중 무엇이 그 이름인가였다. <c>20</c> 을 보여 주는 적은 스물이라고
        /// 불리는 적이 아니고, 화자의 이름을 보여 주는 채팅 창은 그렇게 불리는 컨트롤이 아니다 — 그런데 둘 다 합치기 버튼의
        /// <c>Combine</c> 과 같은 필드에 도착했고, 그 아래의 무엇도 그것들을 가릴 수 없었다. 개발 빌드에서 스물둘 중 열여섯이
        /// 숫자였다.
        ///
        /// 단어가 어떻게 생겼는지가 아니라 객체가 무엇인지로 답한다. 플레이어가 누를 수 있는 것 아래의 텍스트는 눌리는 그것 위에
        /// 쓰인 것이고, 무엇이라 적혀 있든 그것은 캡션이다. 그 밖의 자리에 있는 텍스트는 게임이 그 순간 보여 주고 있는 것이다.
        /// 문자열의 모양으로 추측하는 것은 — "숫자는 이름이 아니다" — 여기서는 맞고 숫자로 라벨을 단 첫 버튼에서 틀린다.
        ///
        /// 이 파일의 나머지와 같은 이유로 타입 이름으로 맞춘다: 이 어셈블리는 uGUI 에 대고 빌드되지 않고, 그것이 없는
        /// 프로젝트도 여전히 컴파일돼야 한다.
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
        /// 텍스트 컴포넌트가 보여 주고 있는 문자열. 그것에 대고 빌드하지 않은 채로 읽는다.
        /// </summary>
        /// <remarks>
        /// uGUI 와 TextMeshPro 는 프로젝트에 없을 수 있는 패키지이고 이 어셈블리는 둘 다 참조하지 않는다 — 리포트가 이미
        /// 컴파일 대상 타입이 아니라 <c>GetType().FullName</c> 으로 컴포넌트의 이름을 대는 것과 같은 이유다. 기반 타입으로
        /// 맞추면 둘 중 어느 이름도 대지 않고 <c>TextMeshProUGUI</c> 와 <c>TextMeshPro</c> 를 덮고, 둘 다 난독화가 건드리지
        /// 않는 엔진 쪽 타입이다.
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
                // 던지는 프로퍼티는 컴포넌트 하나이지, 객체를 잃을 이유가 아니다.
                return null;
            }
        }

        private static bool IsLabel(Type type)
        {
            return Derives(type, "UnityEngine.UI.Text") || Derives(type, "TMPro.TMP_Text");
        }

        /// <summary>
        /// 컴포넌트 위에 그려진 그림의 이름. 그것이 Unity 가 준 것이 아닐 때.
        /// </summary>
        /// <remarks>
        /// Unity 는 제 것을 그리지 않은 사람을 위해 스프라이트 몇 개를 함께 보내고, 그중 하나를 그대로 둔 버튼은 아무도 이름을
        /// 붙이지 않은 버튼이다 — <c>Button (Legacy)</c> 가 제 객체에 대해 말하는 것과 같다. <c>UISprite</c> 를 보고하면
        /// 화면에도 없고 게임에도 없는 단어를 테스트 단계에 넣게 된다.
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

        /// <summary>컴포넌트 하나를 쓰고, 할 말이 있었는지 말한다.</summary>
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

            // 컴포넌트 대부분은 배경이다. 모든 transform 과 스프라이트를 쓰면 테스트가 작용할 수 있는 몇 개가 파묻힌다.
            if (string.IsNullOrEmpty(evidence) && calls.Count == 0)
            {
                return false;
            }

            var refs = new List<Reference>();

            // 애초에 쓸 값이 있는 컴포넌트에 대해서만. 씬의 모든 스프라이트와 콜라이더의 모든 참조를 읽는 것은 아무 말도 하지 않기
            // 위해 씬 전체만큼의 값을 치르는 일이다.
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

            // 근거는 여기가 아니라 이 이름 아래의 표에 들어간다. 한 종류의 슬라임 열다섯은 작용할 자리 열다섯이고 그것들에 대해
            // 알아야 할 것은 하나다.
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

                // 이 컴포넌트 자신의 타입에 근거가 있을 때도 적어 둔다. 배선이 가리키는 것은 그 배선을 쥔 타입과 다른 타입이다.
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

                // 프리팹과 씬 루트는 예전에 같은 방식으로 쓰였다. 이제는 대놓고 말한다. 둘 중 하나만이 테스트에게 가라고 시킬 수 있는
                // 자리이기 때문이다.
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

                // 소유자를 손에 쥐고 있는 동안 알아내고, 프리팹은 ScriptableObject 를 거쳐 쥐고 있는 일이 많으므로 한두 걸음 더
                // 따라간다. 리포트는 이것을 반대 방향으로 필요로 하는데 — 아무도 만나지 못한 타입에서 그것을 만들어낼 필드로 —
                // 씬을 뒤로하고 나면 그 물음을 물을 수 없다.
                if (reference.Asset)
                {
                    try
                    {
                        SerializedReferences.Trace(reference.Held, type, reference.Field);
                    }
                    catch (Exception)
                    {
                        // 걸을 수 없는 애셋 하나가 씬 서술을 멈출 이유는 아니다.
                    }
                }
            }

            text.Append("]}");
            return true;
        }

        /// <summary>어떤 타입을 처음 만났을 때 그 타입의 근거를 표에 넣는다.</summary>
        /// <remarks>
        /// 문서는 이미 조립된 채로 도착한다 — 타입마다 배열 하나이고, 분석기가 그렇게 써서 통째로 날라 온다. 있는 그대로
        /// 통과시킨다. 문자열로 따옴표를 씌우면 이것을 읽는 쪽이 그것을 다시 벗겨야 한다.
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
