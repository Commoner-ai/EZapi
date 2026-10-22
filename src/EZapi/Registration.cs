namespace EZapi.Registration;

/// <summary>初始化期声明的提交状态；数值与当前 Forge 公共契约一致。</summary>
public enum SubmitStatus
{
    Accepted = 0,
    AlreadyPresent = 1,
    Invalid = 2,
    Conflict = 3
}

/// <summary>注册结果。Accepted/AlreadyPresent 表示声明已接收；具体依赖和原生应用状态须查询相应API。</summary>
public sealed record SubmitResult(SubmitStatus Status, string Reason)
{
    public bool Accepted => Status is SubmitStatus.Accepted or SubmitStatus.AlreadyPresent;
}

/// <summary>已登记的开局身份到游戏原生身份类型的不可变映射。</summary>
public sealed record StartRegistrationView(string OwnerId, string StartId, int NativeStartType);
