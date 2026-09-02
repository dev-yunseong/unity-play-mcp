using System.Runtime.CompilerServices;

// 설정 파일을 고치는 형식 변환은 이 어셈블리 밖에서 부를 일이 없어 전부 internal 이다.
// 그런데 그 변환이 곧 사용자의 설정 파일과의 계약이라, 검증하려면 불러야 한다. 테스트에만 연다.
[assembly: InternalsVisibleTo("Artel.McpConfig.Editor.Tests")]
