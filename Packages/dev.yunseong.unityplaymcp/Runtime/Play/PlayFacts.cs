using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnityPlayMcp.Play
{
    /// <summary>
    /// 기본 어댑터의 사실: 화면에 보이는 값과 UI 컨트롤의 상태. 그리고 조건 평가를 위한 멤버 읽기.
    /// </summary>
    /// <remarks>
    /// 임의 private field 를 나열하지 않는다. player scope 는 Unity 기본 UI 컴포넌트의 공개 멤버만 읽고, debug scope 는
    /// 공개 필드/속성으로 넓힌다. 그 밖의 값은 provider 가 사실로 내놓아야 한다. 비밀값 제거는 MCP server 가 항상 한다.
    /// </remarks>
    internal static class PlayFacts
    {
        private static readonly string[] PlayerNamespaces = { "UnityEngine.UI", "TMPro" };

        public static void AddRuntimeFacts(GameObject entity, EntityRefDto handle, List<FactDto> facts)
        {
            var text = entity.GetComponent<Text>();
            if (text != null)
            {
                facts.Add(Fact(handle, "Text", "text", "text", text.text));
            }

            var tmp = entity.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                facts.Add(Fact(handle, tmp.GetType().Name, "text", "text", tmp.text));
            }

            var toggle = entity.GetComponent<Toggle>();
            if (toggle != null)
            {
                facts.Add(Fact(handle, "Toggle", "isOn", "isOn", toggle.isOn));
            }

            var slider = entity.GetComponent<Slider>();
            if (slider != null)
            {
                facts.Add(Fact(handle, "Slider", "value", "value", slider.value));
                facts.Add(Fact(handle, "Slider", "minValue", "minValue", slider.minValue));
                facts.Add(Fact(handle, "Slider", "maxValue", "maxValue", slider.maxValue));
            }

            var input = entity.GetComponent<InputField>();
            if (input != null)
            {
                facts.Add(Fact(handle, "InputField", "text", "text", input.text));
            }

            var tmpInput = entity.GetComponent<TMP_InputField>();
            if (tmpInput != null)
            {
                facts.Add(Fact(handle, "TMP_InputField", "text", "text", tmpInput.text));
            }

            var image = entity.GetComponent<Image>();
            if (image != null && image.type == Image.Type.Filled)
            {
                facts.Add(Fact(handle, "Image", "fillAmount", "fillAmount", image.fillAmount));
            }
        }

        private static FactDto Fact(EntityRefDto handle, string component, string member, string name, object value)
        {
            return new FactDto
            {
                Name = name,
                Value = value,
                Status = "known",
                Source = "runtime",
                Evidence = new EvidenceDto { Entity = handle, Component = component, Member = member }
            };
        }

        /// <summary>
        /// 컴포넌트의 공개 멤버 하나를 읽는다. 읽을 수 없으면 이유와 함께 unknown/unsupported 로 돌려준다.
        /// </summary>
        public static MemberSampleDto ReadMember(GameObject entity, string componentName, string memberName, bool playerScope)
        {
            var sample = new MemberSampleDto { Component = componentName, Member = memberName };
            Component match = null;
            var matches = 0;
            foreach (var component in entity.GetComponents<Component>())
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type.Name == componentName || type.FullName == componentName)
                {
                    match = component;
                    matches++;
                }
            }

            if (matches == 0)
            {
                sample.Status = "unknown";
                sample.Reason = "the entity has no component " + componentName;
                return sample;
            }

            if (matches > 1)
            {
                sample.Status = "unknown";
                sample.Reason = "the entity has " + matches + " components of type " + componentName;
                return sample;
            }

            var componentType = match.GetType();
            if (playerScope && !IsPlayerReadable(componentType))
            {
                sample.Status = "unsupported";
                sample.Reason = "player scope reads only built-in UI components; use debug scope or a provider fact";
                return sample;
            }

            object value;
            Type valueType;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var field = componentType.GetField(memberName, flags);
            var property = field == null ? componentType.GetProperty(memberName, flags) : null;
            try
            {
                if (field != null)
                {
                    value = field.GetValue(match);
                    valueType = field.FieldType;
                }
                else if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(match, null);
                    valueType = property.PropertyType;
                }
                else
                {
                    sample.Status = "unknown";
                    sample.Reason = componentName + " has no public member " + memberName;
                    return sample;
                }
            }
            catch (Exception exception)
            {
                sample.Status = "unknown";
                sample.Reason = "reading " + memberName + " threw " + exception.GetType().Name;
                return sample;
            }

            if (!PlaySemantics.IsPlainValue(value) && !(value == null && valueType == typeof(string)))
            {
                sample.Status = "unsupported";
                sample.Reason = "type " + valueType.Name + " is not a bool, number, string or enum";
                return sample;
            }

            sample.Status = "known";
            sample.Value = value is Enum ? value.ToString() : value;
            sample.ValueType = valueType.IsEnum ? "enum" : TypeName(valueType);
            return sample;
        }

        private static bool IsPlayerReadable(Type type)
        {
            var ns = type.Namespace ?? string.Empty;
            foreach (var allowed in PlayerNamespaces)
            {
                if (ns == allowed || ns.StartsWith(allowed + ".", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TypeName(Type type)
        {
            if (type == typeof(string))
            {
                return "string";
            }

            if (type == typeof(bool))
            {
                return "boolean";
            }

            return "number";
        }
    }
}
