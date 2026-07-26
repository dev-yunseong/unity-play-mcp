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
        public void Serialize_AllScenesCarriesBuildIndexAndPathPerScene()
        {
            var message = new AllScenesMessageDto
            {
                Type = "ALL_SCENES",
                Id = 4,
                Scenes = new List<ScannedSceneDto>
                {
                    new ScannedSceneDto
                    {
                        BuildIndex = 0,
                        Path = "Assets/Scenes/Lobby.unity",
                        Scene = new SceneDto { Id = 1, Type = "scene", Name = "Lobby" }
                    },
                    new ScannedSceneDto
                    {
                        BuildIndex = 1,
                        Path = "Assets/Scenes/Game.unity",
                        Scene = new SceneDto { Id = 2, Type = "scene", Name = "Game" }
                    }
                }
            };

            var root = JObject.Parse(JsonConvert.SerializeObject(message));

            Assert.That((string)root["type"], Is.EqualTo("ALL_SCENES"));
            Assert.That(root["scenes"], Has.Count.EqualTo(2));
            Assert.That((int)root["scenes"]?[1]?["buildIndex"], Is.EqualTo(1));
            Assert.That((string)root["scenes"]?[1]?["path"], Is.EqualTo("Assets/Scenes/Game.unity"));

            // Each entry nests the same scene shape GAME_STATE sends, so a server parses one form.
            Assert.That((string)root["scenes"]?[1]?["scene"]?["type"], Is.EqualTo("scene"));
            Assert.That((string)root["scenes"]?[1]?["scene"]?["name"], Is.EqualTo("Game"));
            Assert.That(root["scenes"]?[1]?["scene"]?["children"], Is.TypeOf<JArray>());
        }

        [Test]
        public void Serialize_BlockCarriesActiveFlag()
        {
            var active = JObject.Parse(JsonConvert.SerializeObject(new SceneBlockDto { Name = "panel" }));
            var inactive = JObject.Parse(
                JsonConvert.SerializeObject(new SceneBlockDto { Name = "panel", Active = false }));

            // Present on every block, not only the ones a full scan reveals, so a server reads one
            // shape either way.
            Assert.That((bool)active["active"], Is.True);
            Assert.That((bool)inactive["active"], Is.False);
        }

        [Test]
        public void Serialize_TrackedStateCarriesLoweredFieldValues()
        {
            var component = new TrackedComponentDto
            {
                ComponentType = "Game.PlayerController",
                Name = "PlayerController",
                States = new List<StateDto>
                {
                    new StateDto
                    {
                        Tag = string.Empty,
                        Name = "Spawn",
                        Type = "UnityEngine.Vector3",
                        Value = new Dictionary<string, object> { { "x", 1f }, { "y", 2f }, { "z", 3f } }
                    }
                }
            };

            var json = JObject.Parse(JsonConvert.SerializeObject(component));
            var state = json["states"]?[0];

            Assert.That((string)json["type"], Is.EqualTo("Game.PlayerController"));
            Assert.That((string)state?["tag"], Is.EqualTo(string.Empty));
            Assert.That((float)state?["value"]?["y"], Is.EqualTo(2f));
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
