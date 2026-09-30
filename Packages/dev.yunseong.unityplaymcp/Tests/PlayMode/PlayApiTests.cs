using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityPlayMcp.Play;
using UnityPlayMcp.Protocol.Dto;

namespace UnityPlayMcp.Tests
{
    /// <summary>
    /// observe/act 도구 묶음의 Unity 쪽 wire method. 게임 클래스 없이 3D collider 와 provider 만으로 확인한다.
    /// </summary>
    /// <remarks>
    /// 결과는 wire 에서처럼 JSON 으로 직렬화한 뒤 읽는다. 그래서 필드 이름이 MCP server 의 기대와 어긋나면 여기서 실패한다.
    /// 렌더러 가시성은 batch 실행에서 참이 되지 않을 수 있어 player scope 의 시각 판정은 시험하지 않는다.
    /// </remarks>
    public sealed class PlayApiTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private PlayApi api;

        [SetUp]
        public void SetUp()
        {
            PlaySemantics.ResetForTests();
            api = new PlayApi(null);
        }

        [TearDown]
        public void TearDown()
        {
            PlaySemantics.ResetForTests();
            foreach (var item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();
        }

        private IEnumerator Call(string method, System.Action<ActionResultDto> completed, string clientId, params object[] parameters)
        {
            yield return api.Execute(1, method, new List<object>(parameters), clientId, completed);
        }

        private static JObject Json(ActionResultDto result)
        {
            Assert.That(result.IsSuccess, Is.True, result.Error);
            return JObject.FromObject(result.ReturnValue);
        }

        private GameObject Cube(string name, Vector3 position)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            spawned.Add(cube);
            return cube;
        }

        private Camera MainCamera()
        {
            var cameraObject = new GameObject("play api camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            spawned.Add(cameraObject);
            return cameraObject.GetComponent<Camera>();
        }

        private static JObject Observe(ActionResultDto result)
        {
            return Json(result);
        }

        private static Dictionary<string, object> Request(string scope, string name = null, int max = 50)
        {
            var request = new Dictionary<string, object>
            {
                { "scope", scope }, { "maxEntities", max }, { "factsPerEntity", 20 }
            };
            if (name != null)
            {
                request["filter"] = new Dictionary<string, object> { { "name", name } };
            }

            return request;
        }

        [UnityTest]
        public IEnumerator Capabilities_ReportsTheProtocolSessionAndTheNewTools()
        {
            var result = default(ActionResultDto);
            yield return Call("play_capabilities", r => result = r, "c");

            var body = Json(result);
            Assert.That((int)body["protocolVersion"], Is.EqualTo(PlayApi.ProtocolVersion));
            Assert.That((string)body["sessionId"], Is.EqualTo(api.SessionId));
            var tools = body["tools"].Select(token => (string)token).ToList();
            Assert.That(tools, Is.SupersetOf(new[] { "observe", "act_and_observe", "watch_events", "query_space" }));
            Assert.That(body["observationPolicies"].Select(token => (string)token), Is.EquivalentTo(new[] { "player", "debug" }));
        }

        [UnityTest]
        public IEnumerator Observe_ReturnsAFreshStampEvenWhenNothingChangedAndKeepsHandlesStable()
        {
            MainCamera();
            Cube("solo", Vector3.zero);
            yield return null;

            var first = default(ActionResultDto);
            yield return Call("play_observe", r => first = r, "c", Request("debug", "solo"));
            yield return null;
            var second = default(ActionResultDto);
            yield return Call("play_observe", r => second = r, "c", Request("debug", "solo"));

            var a = Observe(first);
            var b = Observe(second);
            Assert.That((int)b["stamp"]["frame"], Is.GreaterThan((int)a["stamp"]["frame"]), "a new sample stamp on every call");
            Assert.That((bool)a["coherent"], Is.True);
            Assert.That(a["entities"].Count(), Is.EqualTo(1));
            Assert.That(a["entities"][0]["ref"].ToString(), Is.EqualTo(b["entities"][0]["ref"].ToString()), "the same object keeps its handle");
            Assert.That((string)a["stamp"]["sessionId"], Is.EqualTo(api.SessionId));
            Assert.That((string)a["policy"]["scope"], Is.EqualTo("debug"));
            Assert.That((string)a["policy"]["visibilityGuarantee"], Is.EqualTo("none"));
            Assert.That((string)a["entities"][0]["bounds"]["source"], Is.EqualTo("collider3d").Or.EqualTo("renderer"));
        }

        [UnityTest]
        public IEnumerator Observe_AppliesTheEntityBudgetAndSaysHowManyWereLeftOut()
        {
            MainCamera();
            for (var i = 0; i < 5; i++)
            {
                Cube("many" + i, new Vector3(i, 0f, 0f));
            }

            yield return null;
            var result = default(ActionResultDto);
            yield return Call("play_observe", r => result = r, "c", Request("debug", "many", 2));

            var body = Observe(result);
            Assert.That(body["entities"].Count(), Is.EqualTo(2));
            Assert.That((int)body["omittedEntities"], Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator Observe_ClassifiesAnEntityThatLeftTheResultInsteadOfCallingItDestroyed()
        {
            MainCamera();
            var kept = Cube("track kept", Vector3.zero);
            var removed = Cube("track removed", Vector3.right);
            yield return null;
            var first = default(ActionResultDto);
            yield return Call("play_observe", r => first = r, "c", Request("debug", "track"));
            var refs = Observe(first)["entities"].Select(entity => entity["ref"]).ToList();

            removed.SetActive(false);
            var request = Request("debug", "kept");
            request["track"] = refs;
            var second = default(ActionResultDto);
            yield return Call("play_observe", r => second = r, "c", request);

            var lifecycles = (JObject)Observe(second)["lifecycles"];
            Assert.That(lifecycles.Properties().Count(), Is.EqualTo(1));
            Assert.That((string)lifecycles.Properties().First().Value, Is.EqualTo("inactive"), "deactivated is not destroyed");
            Assert.That(kept, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Sample_DistinguishesDestroyedInactiveAndAmbiguous()
        {
            MainCamera();
            var doomed = Cube("doomed", Vector3.zero);
            Cube("twin", Vector3.left);
            Cube("twin", Vector3.right);
            yield return null;
            var observed = default(ActionResultDto);
            yield return Call("play_observe", r => observed = r, "c", Request("debug", "doomed"));
            var handle = Observe(observed)["entities"][0]["ref"];

            Object.Destroy(doomed);
            yield return null;

            var sampled = default(ActionResultDto);
            yield return Call("play_sample", r => sampled = r, "c", new Dictionary<string, object>
            {
                { "scope", "debug" },
                { "targets", new List<object>
                    {
                        new Dictionary<string, object> { { "key", "gone" }, { "ref", handle } },
                        new Dictionary<string, object> { { "key", "dup" }, { "name", "twin" } },
                        new Dictionary<string, object> { { "key", "nobody" }, { "name", "no such object" } }
                    }
                }
            });

            var targets = Json(sampled)["targets"];
            Assert.That((string)targets["gone"]["lifecycle"], Is.EqualTo("destroyed"));
            Assert.That((string)targets["dup"]["status"], Is.EqualTo("ambiguous"));
            Assert.That((int)targets["dup"]["count"], Is.EqualTo(2));
            Assert.That((string)targets["nobody"]["status"], Is.EqualTo("none"));
        }

        [UnityTest]
        public IEnumerator Sample_ReadsPlainMembersAndReportsTheRestAsUnsupportedOrUnknown()
        {
            MainCamera();
            var cube = Cube("reader", Vector3.zero);
            cube.AddComponent<Rigidbody>();
            yield return null;
            var observed = default(ActionResultDto);
            yield return Call("play_observe", r => observed = r, "c", Request("debug", "reader"));
            var handle = Observe(observed)["entities"][0]["ref"];

            var sampled = default(ActionResultDto);
            yield return Call("play_sample", r => sampled = r, "c", new Dictionary<string, object>
            {
                { "scope", "debug" },
                { "targets", new List<object> { new Dictionary<string, object> { { "key", "t" }, { "ref", handle } } } },
                { "members", new List<object>
                    {
                        new Dictionary<string, object> { { "target", "t" }, { "component", "Rigidbody" }, { "member", "mass" } },
                        new Dictionary<string, object> { { "target", "t" }, { "component", "Rigidbody" }, { "member", "position" } },
                        new Dictionary<string, object> { { "target", "t" }, { "component", "Nope" }, { "member", "x" } }
                    }
                }
            });
            var member = Json(sampled)["members"];

            Assert.That((string)member[0]["status"], Is.EqualTo("known"));
            Assert.That((string)member[0]["valueType"], Is.EqualTo("number"));
            Assert.That((string)member[1]["status"], Is.EqualTo("unsupported"), "Vector3 is not a plain value");
            Assert.That((string)member[2]["status"], Is.EqualTo("unknown"), "a missing component is unknown, not zero");
        }

        [UnityTest]
        public IEnumerator Provider_FactsFollowTheirDeclaredVisibilityAndAThrowingProviderIsIsolated()
        {
            MainCamera();
            var entity = Cube("provided", Vector3.zero);
            PlaySemantics.Register(new FixtureProvider("game.fixture", entity));
            PlaySemantics.Register(new ThrowingProvider());
            yield return null;

            var debug = default(ActionResultDto);
            yield return Call("play_observe", r => debug = r, "c", Request("debug", "provided"));

            var facts = Observe(debug)["entities"][0]["facts"].Select(fact => (string)fact["name"]).ToList();
            Assert.That(facts, Does.Contain("game.fixture/energy"));
            Assert.That(facts, Does.Contain("game.fixture/debugOnly"));
            var relation = Observe(debug)["entities"][0]["relations"];
            Assert.That((string)relation[0]["name"], Is.EqualTo("fixture:owner"));

            var stateFact = Observe(debug)["entities"][0]["facts"].First(fact => (string)fact["name"] == "game.fixture/cost");
            Assert.That((string)stateFact["status"], Is.EqualTo("unknown"));
            Assert.That(stateFact["value"], Is.Null, "an unknown value carries no value");
            Assert.That((string)stateFact["source"], Is.EqualTo("provider"));

            var statuses = PlaySemantics.Statuses();
            Assert.That(statuses.Find(status => status.Id == "game.throwing").LastError, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Inspect_ReturnsTheProviderRecipeWithOutcomePredicatesAndSeparatesExpectedFromObserved()
        {
            MainCamera();
            var entity = Cube("provided", Vector3.zero);
            PlaySemantics.Register(new FixtureProvider("game.fixture", entity));
            yield return null;
            var observed = default(ActionResultDto);
            yield return Call("play_observe", r => observed = r, "c", Request("debug", "provided"));
            var action = Observe(observed)["entities"][0]["actions"].First(item => (string)item["source"] == "provider");

            var inspected = default(ActionResultDto);
            yield return Call("play_inspect", r => inspected = r, "c",
                new Dictionary<string, object> { { "actionRef", (string)action["actionRef"] }, { "scope", "debug" } });

            var body = Json(inspected);
            Assert.That((string)body["availability"], Is.EqualTo("available"));
            Assert.That(body["recipe"].Count(), Is.EqualTo(1));
            Assert.That((string)body["recipe"][0]["method"], Is.EqualTo("key_click"));
            Assert.That(body["outcomePredicates"].Count(), Is.EqualTo(1));
            Assert.That(body["expectedEffects"].Count(), Is.EqualTo(1));
            Assert.That(body["observedEffects"].Type, Is.EqualTo(JTokenType.Null), "nothing was observed yet");
            Assert.That((string)body["costs"][0]["name"], Is.EqualTo("energy"));
        }

        [UnityTest]
        public IEnumerator Inspect_FailsAsStaleForAnActionOfADestroyedEntity()
        {
            MainCamera();
            var entity = Cube("short lived", Vector3.zero);
            yield return null;
            var observed = default(ActionResultDto);
            yield return Call("play_observe", r => observed = r, "c", Request("debug", "short lived"));
            var actionRef = (string)Observe(observed)["entities"][0]["actions"][0]["actionRef"];
            Object.DestroyImmediate(entity);

            var inspected = default(ActionResultDto);
            yield return Call("play_inspect", r => inspected = r, "c", new Dictionary<string, object> { { "actionRef", actionRef } });

            Assert.That(inspected.IsSuccess, Is.False);
            Assert.That(inspected.Error, Does.StartWith("stale_ref"));
        }

        [UnityTest]
        public IEnumerator QuerySpace_HitTestsThePhysicsObjectAtTheScreenCenter()
        {
            var camera = MainCamera();
            var cube = Cube("center", Vector3.zero);
            cube.transform.localScale = new Vector3(4f, 4f, 4f);
            yield return null;

            var result = default(ActionResultDto);
            yield return Call("play_query_space", r => result = r, "c", new Dictionary<string, object>
            {
                { "kind", "hit_test" }, { "scope", "debug" },
                { "point", new Dictionary<string, object>
                    { { "space", "screen" }, { "x", Screen.width / 2f }, { "y", Screen.height / 2f } } }
            });

            var body = Json(result);
            Assert.That((string)body["topHit"]["layer"], Is.EqualTo("physics3d"));
            Assert.That((string)body["topHit"]["label"], Is.EqualTo("center"));
            Assert.That(camera, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator QuerySpace_ProjectsWorldToScreenAndRefusesToGuessWithSeveralCameras()
        {
            MainCamera();
            yield return null;

            var projected = default(ActionResultDto);
            yield return Call("play_query_space", r => projected = r, "c", new Dictionary<string, object>
            {
                { "kind", "project_world_to_screen" },
                { "point", new Dictionary<string, object> { { "space", "world3d" }, { "x", 0f }, { "y", 0f }, { "z", 0f } } }
            });
            var body = Json(projected);
            Assert.That((bool)body["onScreen"], Is.True);
            Assert.That((bool)body["orthographic"], Is.False);
            Assert.That((float)body["screen"]["x"], Is.EqualTo(Screen.width / 2f).Within(1f));

            var second = new GameObject("second camera", typeof(Camera));
            spawned.Add(second);
            var ambiguous = default(ActionResultDto);
            yield return Call("play_query_space", r => ambiguous = r, "c", new Dictionary<string, object>
            {
                { "kind", "project_world_to_screen" },
                { "point", new Dictionary<string, object> { { "space", "world3d" }, { "x", 0f }, { "y", 0f }, { "z", 0f } } }
            });
            Assert.That(ambiguous.IsSuccess, Is.False);
            Assert.That(ambiguous.Error, Does.StartWith("ambiguous_camera"));
        }

        [UnityTest]
        public IEnumerator QuerySpace_NeverInventsAGroundPlane()
        {
            MainCamera();
            yield return null;

            var result = default(ActionResultDto);
            yield return Call("play_query_space", r => result = r, "c", new Dictionary<string, object>
            {
                { "kind", "project_screen_to_world" },
                { "point", new Dictionary<string, object> { { "space", "screen" }, { "x", 10f }, { "y", 10f } } }
            });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Does.StartWith("invalid_request"));
            Assert.That(result.Error, Does.Contain("no ground is assumed"));
        }

        [UnityTest]
        public IEnumerator QuerySpace_LineTestReportsPhysicsOcclusionWithoutCallingItAnAttackRule()
        {
            MainCamera();
            var from = Cube("from", new Vector3(-5f, 0f, 0f));
            var to = Cube("to", new Vector3(5f, 0f, 0f));
            Cube("wall", Vector3.zero);
            yield return null;
            var observed = default(ActionResultDto);
            yield return Call("play_observe", r => observed = r, "c", Request("debug", null, 50));
            var refs = Observe(observed)["entities"].ToDictionary(entity => (string)entity["label"], entity => entity["ref"]);

            var result = default(ActionResultDto);
            yield return Call("play_query_space", r => result = r, "c", new Dictionary<string, object>
            {
                { "kind", "line_test" }, { "dimension", "3d" }, { "scope", "debug" },
                { "from", refs["from"] }, { "to", refs["to"] }
            });

            var body = Json(result);
            Assert.That((bool)body["blocked"], Is.True);
            Assert.That((string)body["blockers"][0]["label"], Is.EqualTo("wall"));
            Assert.That((string)body["note"], Does.Contain("not a game rule"));
            Assert.That(from, Is.Not.Null);
            Assert.That(to, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Begin_GivesTheInputToOneClientAndAnswersBusyToTheOther()
        {
            var first = default(ActionResultDto);
            yield return Call("play_begin", r => first = r, "clientA", "op1", "hash", 5000);
            var second = default(ActionResultDto);
            yield return Call("play_begin", r => second = r, "clientB", "op2", "hash", 5000);

            Assert.That((string)Json(first)["status"], Is.EqualTo("acquired"));
            Assert.That(second.IsSuccess, Is.False);
            Assert.That(second.Error, Does.StartWith("busy:op1"));

            string holder;
            Assert.That(api.TryCheckInput("clientA", out holder), Is.True);
            Assert.That(api.TryCheckInput("clientB", out holder), Is.False, "another client's input is refused while op1 runs");

            var end = default(ActionResultDto);
            yield return Call("play_end", r => end = r, "clientA", "op1");
            Assert.That(api.TryCheckInput("clientB", out holder), Is.True);

            var again = default(ActionResultDto);
            yield return Call("play_begin", r => again = r, "clientA", "op1", "hash", 5000);
            Assert.That((string)Json(again)["status"], Is.EqualTo("already_started"), "a retried id is never executed twice");
        }

        [UnityTest]
        public IEnumerator Input_RevisionAdvancesOnlyWhenInputRuns()
        {
            var before = default(ActionResultDto);
            yield return Call("play_begin", r => before = r, "c", "rev1", "h", 1000);
            var revisionBefore = (long)Json(before)["inputRevision"];

            api.NoteInput();
            var after = default(ActionResultDto);
            yield return Call("play_begin", r => after = r, "c2", "rev2", "h", 1000);

            Assert.That(after.IsSuccess, Is.False, "rev1 still holds the input");
            var end = default(ActionResultDto);
            yield return Call("play_end", r => end = r, "c", "rev1");
            yield return Call("play_begin", r => after = r, "c2", "rev2", "h", 1000);
            Assert.That((long)Json(after)["inputRevision"], Is.EqualTo(revisionBefore + 1));
        }

        [UnityTest]
        public IEnumerator Checkpoint_NoticesASceneChangeAndWaitFramesCountsRenderedFrames()
        {
            var same = default(ActionResultDto);
            yield return Call("play_checkpoint", r => same = r, "c", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            var changed = default(ActionResultDto);
            yield return Call("play_checkpoint", r => changed = r, "c", "some other scene");

            Assert.That((bool)Json(same)["sceneChanged"], Is.False);
            Assert.That((bool)Json(changed)["sceneChanged"], Is.True);

            var startFrame = Time.frameCount;
            var waited = default(ActionResultDto);
            yield return Call("wait_frames", r => waited = r, "c", 3);
            Assert.That(waited.IsSuccess, Is.True);
            Assert.That(Time.frameCount - startFrame, Is.GreaterThanOrEqualTo(3));

            var invalid = default(ActionResultDto);
            yield return Call("wait_frames", r => invalid = r, "c", 100000);
            Assert.That(invalid.IsSuccess, Is.False, "the wait is bounded");
        }

        [UnityTest]
        public IEnumerator Events_ProviderEventsCarryTheirFrameAndAreReadByCursor()
        {
            PlaySemantics.Emit("game.fixture", "goal_reached", "goal", null, 1, PlayVisibility.Player);
            yield return null;
            PlaySemantics.Emit("game.fixture", "hidden", null, null, null);

            var first = default(ActionResultDto);
            yield return Call("play_events", r => first = r, "c", 0, 10);
            var body = Json(first);
            Assert.That(body["events"].Count(), Is.EqualTo(2));
            Assert.That((int)body["events"][1]["stamp"]["frame"], Is.GreaterThan((int)body["events"][0]["stamp"]["frame"]));
            Assert.That((bool)body["events"][0]["playerVisible"], Is.True);
            Assert.That((bool)body["events"][1]["playerVisible"], Is.False);

            var rest = default(ActionResultDto);
            yield return Call("play_events", r => rest = r, "c", (long)body["next"], 10);
            Assert.That(Json(rest)["events"].Count(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Observe_DoesNotIncludeTheSdksOwnOverlays()
        {
            var overlay = new GameObject("Unity Play MCP Virtual Cursor Canvas");
            overlay.AddComponent<BoxCollider>();
            spawned.Add(overlay);
            yield return null;

            var result = default(ActionResultDto);
            yield return Call("play_observe", r => result = r, "c", Request("debug", "Unity Play MCP"));

            Assert.That(Observe(result)["entities"].Count(), Is.Zero);
        }

        private sealed class FixtureProvider : PlaySemanticProvider
        {
            private readonly string id;
            private readonly GameObject entity;

            public FixtureProvider(string id, GameObject entity)
            {
                this.id = id;
                this.entity = entity;
            }

            public override string Id { get { return id; } }

            public override void Describe(PlayObservationContext context, PlayProviderOutput output)
            {
                output.AddFact(entity, "energy", 3, "energy", PlayVisibility.Player);
                output.AddFact(entity, "debugOnly", "x");
                output.AddUnknownFact(entity, "cost", "no cost is declared for this fixture", PlayVisibility.Debug);
                output.AddRelation(entity, "fixture:owner", entity, PlayVisibility.Debug);

                var action = new PlayProviderAction
                {
                    Id = "press",
                    Label = "Press the fixture key",
                    Visibility = PlayVisibility.Debug,
                    Available = true
                };
                action.Recipe.Add(PlayRecipeStep.KeyClick(KeyCode.Space, 0.05f));
                action.OutcomeJson.Add(PlayPredicates.EventMatches(id, "pressed"));
                action.Costs.Add(new PlayFactDeclaration("energy", 1, "energy"));
                action.ExpectedEffects.Add(new PlayFactDeclaration("pressed", true));
                output.AddAction(entity, action);
            }
        }

        private sealed class ThrowingProvider : PlaySemanticProvider
        {
            public override string Id { get { return "game.throwing"; } }

            public override void Describe(PlayObservationContext context, PlayProviderOutput output)
            {
                throw new System.InvalidOperationException("provider failure");
            }
        }
    }
}
