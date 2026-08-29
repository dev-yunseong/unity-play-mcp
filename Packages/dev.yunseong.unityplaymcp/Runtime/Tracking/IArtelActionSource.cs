using System.ComponentModel;

namespace Artel.Tracking
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IArtelActionSource
    {
        ActionInvocationBuffer ArtelActionBuffer { get; }
    }
}
