using System.Runtime.CompilerServices;

// 스캔이 문서를 어떻게 적는지는 이 어셈블리 안에서만 뜻이 있는 일이라 진입점이 internal 이다.
// 그런데 그 문서의 모양이 곧 소비자와의 계약이고, 계약을 검증하려면 그 진입점을 불러야 한다.
// 테스트에만 연다.
[assembly: InternalsVisibleTo("Artel.Runtime.Tests")]
