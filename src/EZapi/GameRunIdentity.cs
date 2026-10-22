namespace EZapi;

/// <summary>纯托管周目身份快照。Epoch 区分同一存档的两次载入，不是可保存的周目 ID。</summary>
public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
