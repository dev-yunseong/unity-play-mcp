using System.Runtime.CompilerServices;

// 문서 모양은 소비자와의 계약이므로 테스트가 internal 진입점을 불러 검증한다.
[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime.Tests")]
[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime.PlayModeTests")]

// UnityPlayMcp.Runtime 이 PackageVersion 을 읽는다. version 을 손으로 맞추는 자리를 하나로 유지한다.
[assembly: InternalsVisibleTo("UnityPlayMcp.Runtime")]
