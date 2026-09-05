using System;

namespace MicrobialNet.Story.UI
{
    /// <summary>
    /// 对话框内容视图契约。挂在对话框预制体根（或子）上的组件实现此接口，
    /// 由管理器在 Show 时回调 <see cref="Setup"/> 绑定数据与句柄。
    /// 具体视图自行把 payload 转型为自身业务数据。
    /// </summary>
    public interface IDialogueBoxView
    {
        /// <summary>
        /// 绑定数据。
        /// </summary>
        /// <param name="handle">本对话框句柄，视图可用它请求关闭（handle.Close()）。</param>
        /// <param name="payload">调用方传入的任意数据，由具体视图转型。</param>
        void Setup(DialogueBoxHandle handle, object payload);
    }

    /// <summary>
    /// 可选接口：对话框被回收/销毁前由管理器回调，供视图退订事件、释放资源。
    /// 视图若实现了此接口，管理器在归还对象池前调用 <see cref="OnRecycle"/>。
    /// </summary>
    public interface IDialogueBoxRecyclable
    {
        void OnRecycle();
    }
}
