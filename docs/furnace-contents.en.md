# Native furnace contents and external power — EZapi 0.1.67

Register the provider, initialize each fresh detached unowned connector with InitializeConnector, and wait for IsAvailable. Keep and dispose the registration lease. The existing four-argument Register overload remains available and defaults to MachineMaterialContentsPolicy.Reject.

To intentionally consume a material together with its contents, call ExternalPowerApi.Register(ownerId, connectorId, queryEnergy, planDebit, MachineMaterialContentsPolicy.ConsumeGraph). This policy applies only to the native furnace; EZapi recipes retain their own MaterialContents policy. Query and plan callbacks must be read-only. A default Reject provider can still process a furnace containing an old output with contents: old outputs and their contents are always preserved.

The journal captures descendant identities, state/dictionary identity, tags, value, geometry, parent inventories, inventory boundaries/locks and graph edges. Root slot/handler/position is recorded separately. Descendants are frozen while the original furnace processes roots. An early descendant Destroy request rejects the batch while preserving the original object.

On failure, restore the original provider's real energy, remove new products, restore the original roots and descendant metadata, then read back exact state. A disposed provider remains responsible for recovery. Unconfirmed recovery quarantines the machine. After successful production is verified, consume descendants deepest first, then destroy the consumed root, even if its count has reached zero. Committed cleanup failures quarantine without refund or retry. Managed destruction receipts avoid dereferencing retired wrappers. The bounded two-frame queue only confirms Unity destruction.

Limits:128 original input/output roots,4096 total graph items,8192 nodes/edges and16384 tags per graph,128 modules and128 pending destruction confirmations. Unknown native signatures/builds fail closed. Arbitrary third-party identity/topology corruption is not promised recoverable. No async/network/crash-recovery transaction is provided.

Final build:3 projects,0 warnings/errors,49 offline suites; graph journal42/0 and provider journal66/0. Native furnace28 scenarios233/0;8 groups1126/0. Prior complex machine state19/0 with committed count2 unchanged. All536 ES3 files unchanged. Tests use temporary graphs and the actual native callback, without night/save requests. Previous two-night EZapi external-power persistence evidence remains scoped to those custom machines.

See [Chinese guide](furnace-contents.zh-CN.md), [AI index](furnace-contents.index.json), [public contract audit](external-power-public-contract.audit.json) and [verification](verification/furnace-contents-native-0.1.67.json). English documentation does not claim the unfinished framework-wide native English UI audit is complete.
