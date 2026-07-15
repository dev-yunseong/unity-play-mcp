using System.Collections.Generic;
using Artel.Protocol.Dto;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Artel.Tests.Protocol
{
    public sealed class SceneJsonContractTests
    {
        [Test]
        public void Serialize_UsesBlockComponentStateActionShape()
        {
            var message = new GameStateMessageDto
            {
                Type = "GAME_STATE",
                Id = 1,
                Scene = new SceneDto
                {
                    Id = 1,
                    Type = "scene",
                    Name = "lobby scene",
                    Children = new List<SceneBlockDto>
                    {
                        new SceneBlockDto
                        {
                            Id = 2,
                            Type = "block",
                            Name = "login panel",
                            Components = new List<SceneComponentDto>
                            {
                                new EditTextComponentDto
                                {
                                    Name = "email edit text",
                                    Placeholder = "example@artel.kr",
                                    States = new List<StateDto>
                                    {
                                        new StateDto { Tag = "hp", Name = "hp", Type = "float", Value = 2f }
                                    },
                                    Actions = new List<ActionInvocationDto>
                                    {
                                        new ActionInvocationDto
                                        {
                                            Tag = "attack",
                                            Name = "Attack",
                                            ReturnValue = 3,
                                            Timestamp = "2026-07-15T00:00:00.0000000Z"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var root = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.That((string)root["type"], Is.EqualTo("GAME_STATE"));
            Assert.That(root["scene"]?["children"], Is.TypeOf<JArray>());
            Assert.That(root["scene"]?["childern"], Is.Null);
            Assert.That((string)root["scene"]?["children"]?[0]?["type"], Is.EqualTo("block"));
            Assert.That((string)root["scene"]?["children"]?[0]?["components"]?[0]?["type"], Is.EqualTo("editText"));
            Assert.That((string)root["scene"]?["children"]?[0]?["components"]?[0]?["states"]?[0]?["tag"], Is.EqualTo("hp"));
            Assert.That((string)root["scene"]?["children"]?[0]?["components"]?[0]?["actions"]?[0]?["tag"], Is.EqualTo("attack"));
        }

        [Test]
        public void Serialize_ButtonDoesNotExposeTextFields()
        {
            var json = JsonConvert.SerializeObject(new ButtonComponentDto { Name = "login button" });
            var component = JObject.Parse(json);

            Assert.That((string)component["type"], Is.EqualTo("button"));
            Assert.That(component["content"], Is.Null);
            Assert.That(component["placeholder"], Is.Null);
        }
    }
}
