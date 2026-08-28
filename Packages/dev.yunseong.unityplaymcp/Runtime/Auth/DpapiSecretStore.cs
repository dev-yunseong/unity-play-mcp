#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Artel.Auth
{
    /// <summary>
    /// Windows DPAPI로 암호화한 뒤 사용자 프로필 아래 파일로 둔다.
    /// </summary>
    /// <remarks>
    /// 관리형 <c>ProtectedData</c>는 쓸 수 없다 — .NET Standard 2.1 프로필에 없다. crypt32를
    /// 직접 부르면 프로젝트의 API 호환성 수준과 무관하게 Mono와 IL2CPP 양쪽에서 돈다.
    ///
    /// 자격 증명 관리자(CredWrite) 대신 DPAPI를 고른 이유는 값 길이 제한이 없고 마샬링할
    /// 구조체가 적어서다. 보호 범위는 둘 다 사용자 계정 단위로 같다.
    ///
    /// 파일은 persistentDataPath가 아니라 LocalApplicationData 아래 둔다. SDK 토큰은 앱이
    /// 아니라 사람의 것이라, 같은 사람이 만드는 여러 프로젝트가 한 번의 로그인을 나눠 쓰도록
    /// macOS 키체인 쪽과 범위를 맞춘다.
    /// </remarks>
    internal sealed class DpapiSecretStore : IArtelSecretStore
    {
        // 사람을 부르는 창을 띄우지 않는다. 유니티 메인 스레드에서 부르므로 창이 뜨면
        // 게임이 멈춘 것처럼 보인다.
        private const int CryptProtectUiForbidden = 0x1;

        public bool TryLoad(string key, out string value)
        {
            var path = ResolvePath(key);
            if (!File.Exists(path))
            {
                value = string.Empty;
                return false;
            }

            // 복호화 실패는 삼키지 않고 올린다. 다른 사용자 계정이 만든 파일이거나 깨진
            // 파일인데, 빈 값으로 접으면 원인 없이 로그인만 반복된다. 다시 로그인하면
            // Save가 덮어쓴다.
            value = Encoding.UTF8.GetString(Transform(File.ReadAllBytes(path), true));
            return value.Length > 0;
        }

        public void Save(string key, string value)
        {
            var path = ResolvePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Transform(Encoding.UTF8.GetBytes(value), false));
        }

        public void Delete(string key)
        {
            var path = ResolvePath(key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string ResolvePath(string key)
        {
            // GetFolderPath는 폴더를 찾지 못하면 예외 대신 빈 문자열을 준다. 그대로 Combine하면
            // 경로가 상대 경로가 되어, 암호문이 사용자 프로필이 아니라 실행 파일 옆(에디터에서는
            // 프로젝트 폴더) 에 떨어진다. 소스 트리에 커밋될 수도 있는 자리라 조용히 넘기지 않는다.
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
            {
                throw new InvalidOperationException(
                    "LocalApplicationData 경로를 찾지 못해 토큰을 저장할 곳이 없습니다.");
            }

            return Path.Combine(localAppData, "Artel", "Secrets", key + ".bin");
        }

        /// <summary>
        /// <paramref name="unprotect"/>가 true면 복호화, false면 암호화.
        /// </summary>
        /// <remarks>
        /// 추가 엔트로피는 넣지 않는다. 넣어도 그 값이 함께 배포되는 바이너리 안에 있어
        /// 실제로 막는 것이 없고, macOS 쪽도 같은 사용자면 팝업 없이 읽히므로 양쪽 보호
        /// 범위를 같게 둔다.
        /// </remarks>
        private static byte[] Transform(byte[] input, bool unprotect)
        {
            var dataIn = new DataBlob();
            var dataOut = new DataBlob();
            try
            {
                dataIn.cbData = input.Length;
                dataIn.pbData = Marshal.AllocHGlobal(input.Length);
                Marshal.Copy(input, 0, dataIn.pbData, input.Length);

                var succeeded = unprotect
                    ? CryptUnprotectData(
                        ref dataIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out dataOut)
                    : CryptProtectData(
                        ref dataIn, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out dataOut);

                if (!succeeded)
                {
                    throw new InvalidOperationException(
                        (unprotect ? "DPAPI 복호화" : "DPAPI 암호화") + "에 실패했습니다 (오류 " +
                        Marshal.GetLastWin32Error() + ").");
                }

                var output = new byte[dataOut.cbData];
                Marshal.Copy(dataOut.pbData, output, 0, dataOut.cbData);
                return output;
            }
            finally
            {
                if (dataIn.pbData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(dataIn.pbData);
                }

                // 출력 버퍼는 crypt32가 잡아 준 것이라 LocalFree로 돌려줘야 한다.
                if (dataOut.pbData != IntPtr.Zero)
                {
                    LocalFree(dataOut.pbData);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);
    }
}
#endif
