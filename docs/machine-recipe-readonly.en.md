# Read-only recipe callbacks and product preparation (EZapi0.1.69)

Conditions, count/ID/cost/liquid resolvers, transaction factories and transaction Validate callbacks must be read-only. PrepareItem may configure the new product passed to it; it must not change existing materials, the machine, batteries, liquid containers or old products. Apply performs resource writes; verified commit notifications retain their existing contract.

Before selection and product preparation, EZapi captures the complete machine graph. Plain inputs without contents now have their tags audited as well. The snapshot checks identities, counts, values, ownership, geometry, parents, state/dictionary identities, tags, windows, graph edges and inventory locks. Unchanged quantities do not excuse changed tags.

Changes during selection return EZAPI_RECIPE_STALE. BeforeBatch mutations return EZAPI_MACHINE_AUTOMATION_STALE. Mutations in factories,Validate or Prepare are rejected before Apply, and framework-owned preview products are cleaned up. The guard does not undo arbitrary illegal author writes or repair destroyed native objects. Never destroy objects, load saves or change scenes in these callbacks.

After Apply begins, the selection graph is not used to reject legitimate consumption. Independent resource journals, energy/liquid readbacks and material graphs still verify commit and restoration. Custom transactions remain responsible for their declared material semantics.

Native CreateEmptyItem may return temporary objects with unassigned save ID(-1). Internal observation distinguishes these by pointer. Public SaveGraphApi.Items/Encode/Decode retain strict persisted identity validation. Duplicate assigned IDs,IDs below-1,cycles,dead nodes and size limits still reject. An observation result is not proof that an item can be saved.

Validation:three formal builds with0 warnings/errors and49 offline suites; production-linked recipe480/0,automation281/0,SaveGraph54/0 with explicit native stubs. Native recipes256/0 include23 new scenarios/138 checks; automation135/0 includes two tag-mutation cases. Eight groups total1375/0. Existing machine19/0,still2 commits; all536 ES3 files are byte-identical. No night/save was requested.

The behavior audit now records7 types,30 properties and1 arithmetic helper method. Unreviewed members remain pending. Framework coverage remains37/39 groups; registration/runtime auditing,knowledge-base additions,ease of use,AI indexes and game-English interoperability remain required.
