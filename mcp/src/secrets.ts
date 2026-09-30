import type { JsonValue } from "./pulse.js";

/// 응답에서 가린 값 자리에 들어가는 표시.
///
/// `null` 이나 빈 문자열과 모양이 달라야 agent 가 "값이 없다" 로 읽지 않는다. 키의 `$` 는 게임 값 객체의
/// 필드 이름과 겹치지 않게 한다.
export interface RedactedValue {
  $redacted: true;
  /// 비밀로 판정한 근거. `name` 은 멤버·필드 이름, `value` 는 값의 모양이다.
  because: "name" | "value";
  /// 원문 문자열의 길이. 비었는지(로그인 여부)는 조작에 쓸모 있고, 길이만으로는 값을 복원할 수 없다.
  length: number;
}

/// 이름이 비밀을 가리키는 조각들. 소문자로 이어 붙인 이름 안에서 찾는다 (`JwtToken`, `jwt_token`, `API-KEY`).
const SECRET_NAME_PARTS = [
  "token", "jwt", "password", "passwd", "passphrase", "secret", "apikey", "accesskey", "privatekey",
  "credential", "bearer", "cookie", "sessionid", "sessionkey", "authorization", "authkey", "signature",
];

/// 짧아서 다른 낱말 안에 흔히 들어가는 조각들. `Spinner`, `Hotpot`, `Pinned` 를 가리지 않도록 독립된 낱말일 때만 본다.
const SECRET_NAME_WORDS = ["pin", "otp", "pwd", "auth"];

/// 이름이 비밀처럼 보이는지 판정한다.
///
/// 이름만으로는 부족하다(`SceneContext.Current` 가 토큰일 수 있다). `valueLooksSecret` 과 둘 중 하나라도
/// 걸리면 가린다 (#72).
export function nameLooksSecret(name: string): boolean {
  const words = name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter((word) => word.length > 0);
  const joined = words.join("");
  return SECRET_NAME_PARTS.some((part) => joined.includes(part))
    || SECRET_NAME_WORDS.some((word) => words.includes(word));
}

const JWT = /^[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}$/;
const AUTH_HEADER = /^(bearer|basic)\s+\S{8,}$/i;
const PEM = /-----BEGIN [A-Z ]*(PRIVATE KEY|CERTIFICATE)-----/;

/// 경로, 타입 이름, slug 를 나누는 문자. 나눈 조각이 모두 짧으면 사람이 붙인 이름이다.
const NAME_SEPARATORS = /[/.\-_:\[\]()]/;

/// 사람이 붙인 이름 조각의 길이 상한. 무작위 키는 이보다 긴 조각을 포함한다.
const LONGEST_NAMED_SEGMENT = 24;

/// 값의 모양이 비밀처럼 보이는지 판정한다. 이름으로 잡지 못한 값을 위한 검사다.
///
/// JWT, 인증 header, PEM 과, 공백 없는 32자 이상이면서 글자와 숫자가 섞이고 서로 다른 문자가 16종 이상이며
/// 구분자로 나눈 조각 중 하나가 긴 문자열을 잡는다. 마지막 조건이 없으면 계층 경로
/// (`UI[0]/Canvas[0]/LowerBar[0]/PlayButton[0]`)와 타입 이름(`Game.Tutorial.Step3Handler`)까지 가린다.
export function valueLooksSecret(value: string): boolean {
  if (JWT.test(value) || AUTH_HEADER.test(value) || PEM.test(value)) return true;
  if (value.length < 32 || /\s/.test(value)) return false;
  if (value.split(NAME_SEPARATORS).every((segment) => segment.length <= LONGEST_NAMED_SEGMENT)) return false;
  return /[0-9]/.test(value) && /[A-Za-z]/.test(value) && new Set(value).size >= 16;
}

function redacted(because: RedactedValue["because"], value: string): RedactedValue {
  return { $redacted: true, because, length: value.length };
}

/// 비밀로 보이는 이름 아래에서도 가리지 않는 키. package 가 참조를 싣는 구조(`{"path": …, "active": …}`,
/// `{"is": …}`)의 키라 값이 아니다.
const STRUCTURAL_KEYS = new Set(["path", "is"]);

/// `value` 안의 비밀처럼 보이는 문자열을 모두 가린다. `name` 은 그 값을 가진 멤버·필드의 이름이다.
///
/// 비어 있지 않은 문자열만 가린다. `null` 과 `""` 는 "아직 로그인 안 함" 을 읽을 수 있도록 둔다. 토큰을
/// field 로 가진 struct 도 있으므로 객체는 키를 이름으로, 배열은 같은 이름으로 안쪽까지 내려간다. 이름이
/// 비밀로 보이는 멤버 아래의 값은 안쪽 키 이름이 평범해도 가린다.
export function redactSecrets(name: string, value: JsonValue, underSecretName = false): JsonValue {
  const secretName = underSecretName || nameLooksSecret(name);
  if (typeof value === "string") {
    if (value.length === 0) return value;
    if (secretName) return redacted("name", value) as unknown as JsonValue;
    if (valueLooksSecret(value)) return redacted("value", value) as unknown as JsonValue;
    return value;
  }
  if (Array.isArray(value)) {
    return value.map((item) => redactSecrets(name, item, secretName));
  }
  if (value !== null && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([key, inner]) => [
      key,
      STRUCTURAL_KEYS.has(key) ? redactSecrets(key, inner) : redactSecrets(key, inner, secretName),
    ]));
  }
  return value;
}

/// 표시 문자열이 가려졌을 때 대신 쓰는 말. 검색 후보에서 "글자가 없다" 와 "가렸다" 를 구별하게 한다.
export const REDACTED_TEXT = "[redacted]";

/// 값 안에 가린 값이 있는지 판정한다.
export function holdsRedaction(value: JsonValue | undefined): boolean {
  if (value === REDACTED_TEXT) return true;
  if (Array.isArray(value)) return value.some(holdsRedaction);
  if (value === null || typeof value !== "object") return false;
  return Reflect.get(value, "$redacted") === true || Object.values(value).some(holdsRedaction);
}
