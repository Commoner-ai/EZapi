using System.Text.Json;


using MelonLoader;


using MelonLoader.Utils;


[assembly:MelonInfo(typeof(EZapi.EZapiBootstrap),"EZapi","0.1.76","EZapi")]


[assembly:MelonGame(null,null)]


[assembly:MelonPriority(-10000)]


namespace EZapi;





/// <summary>独立 MelonLoader 入口；每个 JSON 内容包独立捕获错误。不会扫描或加载第三方 DLL。</summary>


public sealed class EZapiBootstrap:MelonMod


{


    private static readonly List<string> Errors=new();


    private readonly EZapiDebugOverlay _debug = new();


    private bool _ready,_storeReadyPending;


    public override void OnInitializeMelon()


    {


        try {

            DeveloperToolsSettings.Load(Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi","developer-tools.json"), message => LoggerInstance.Warning(message));
            DebugRegressionSession.Load(Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi","regression-session.json"), DeveloperToolsSettings.Current.RegressionFixtures, message => LoggerInstance.Warning(message));


            CompatibilityPatches.Start();


            NativeDelegateBridge.Start();


            GameEvents.Start();


            ContentApi.Start();RegistrationApi.Start();


            DepartureApi.Start();LifecycleApi.Start();ReadingApi.Start();DialogueApi.Start();PhoneApi.Start();ClientVisitApi.Start();SupplierApi.Start();


            ModuleApi.Start();


            EffectApi.Start();


            InventoryRuntime.Start();InventoryEndpointApi.Start();ExternalPowerApi.Start();


            PresentationApi.Start();


            NpcStockRuntime.Start();ItemStockRuntime.Start();


            ItemRegistry.Start();


            StartApi.Start();


            WorkshopApi.Start();


            if (DeveloperToolsSettings.Current.RegressionFixtures) DebugWorkshop.Register();


            MachineLayoutApi.Start();RecipeGuideApi.Start();MachineTransactionApi.Start();MachineRecipeApi.Start();MachineRegistrationApi.Start();MachineAutomationApi.Start();MachineRuntimeApi.Start();


            LootTables.Start();


            ManufacturingTerminalRuntime.Register();


            ContentPackLoader.LoadAll(Path.Combine(MelonEnvironment.ModsDirectory,"EZapiPacks"),error=>Errors.Add(error));


            if (DeveloperToolsSettings.Current.RegressionFixtures) {
            DebugDeparture.Register();DebugRegistration.Register();DebugNpcStock.Register();DebugItemStock.Register();


            DebugNativeItems.Register();DebugInventoryEndpoint.Register();DebugMachineLayouts.Register();DebugMachineMaterials.Register();DebugMachineRegistration.Register();DebugMachineNight.Register();DebugMachineConditions.Register();DebugMachinePersistence.Register();


            DebugSpriteAssets.Register();


            DebugMachineBatteryRequirements.Register();
            }


            if (DeveloperToolsSettings.Current.Enabled) _debug.Start();


            _ready=true;


        } catch(Exception e) {
            _debug.Stop(); DeveloperToolsSettings.Reset(); DebugRegressionSession.Reset();


            NativeUiPreviewApi.Close();WildNetworkBrowser.Stop();DebugSaveGraph.Clear();DebugRegistration.Clear();RegistrationApi.Stop();


            Errors.Add("initialize: "+e.Message);


            LoggerInstance.Error("EZAPI_INIT_FAILED: "+e);


            DepartureApi.Stop();ClientVisitApi.Stop();SupplierApi.Stop();DebugWorkshop.Clear();WorkshopApi.Stop();MachineRuntimeApi.Stop();MachineAutomationApi.Stop();MachineRegistrationApi.Stop();MachineRecipeApi.Stop();MachineTransactionApi.Stop();RecipeGuideApi.Stop();MachineLayoutApi.Stop();LootTables.Stop();StartApi.Stop();PresentationApi.Stop();DeclarativeItemLocalization.Stop();ItemRegistry.Stop();InventoryEndpointRuntime.Stop();ExternalPowerApi.Stop();InventoryRuntime.Stop();ModuleApi.Stop();ReadingApi.Stop();DialogueApi.Stop();PhoneApi.Stop();LifecycleApi.Stop();EffectApi.Stop();ContentApi.Stop();GameEvents.Stop();NativeDelegateBridge.Stop();CompatibilityPatches.Stop(); DebugSpriteAssets.Clear();SpriteAssets.Clear();LegacySpriteAdapter.Clear();


        }


        WriteDiagnostics();


        if(_ready)LoggerInstance.Msg("EZapi "+FrameworkInfo.Version+" initialized through official hooks. Developer tools="+DeveloperToolsSettings.Current.Enabled+", regression fixtures="+DeveloperToolsSettings.Current.RegressionFixtures+". JSON protocol 0.1; no automatic save requests.");


    }


    internal static bool WriteDiagnostics()


    {


        try {


            var folder=Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi");Directory.CreateDirectory(folder);


            File.WriteAllText(Path.Combine(folder,"diagnostics.json"),JsonSerializer.Serialize(


                new{version="0.1.76-usability",registrations=RegistrationApi.Report(),registrationDebug=DebugRegistration.LastReport,progressionDebug=DebugProgression.LastReport,saveGraphDebug=DebugSaveGraph.LastReport,saveGraph=new{canDecode=SaveGraphApi.CanDecode,reason=SaveGraphApi.DecodeUnavailableReason},debugUi=EZapiDebugOverlay.Report(),uiPreview=NativeUiPreviewApi.Report(),departureButtons=DepartureApi.Snapshot(),wildBrowser=WildNetworkBrowser.Report(),integration=new{


                    host="MelonLoader IL2CPP .NET 6",directory="ModHook.OnModItemDirectoryInit",tooltip="ModHook.OnCreateTooltipEarly/Late",


                    stack="GameItem.mayStackItemFunc (per instance)",sprite="SpriteAssets / LegacySpriteAdapter: isolated public atlasCache + GameItem.SetSprite; owned SpriteDict portrait registration with GetSprite readback",


                    harmony="custom type display name + debug input + inventory guard groups + exact lifecycle group",runData="PlayerStore.modData compatibility bridge; no SaveGame calls",saveMigration="none"},


                    runtimeCapabilities=RuntimeCapabilities.Current,inventoryEndpoints=InventoryEndpointApi.Report(),inventoryEndpointDebug=DebugInventoryEndpoint.LastReport,spriteAssetsDebug=DebugSpriteAssets.LastReport,startRegistrations=StartApi.Report(),workshop=WorkshopApi.Report(),workshopDebug=DebugWorkshop.LastReport,runPurchase=RunPurchaseApi.Report(),runPurchaseDebug=DebugRunPurchase.LastReport,clientVisits=ClientVisitApi.Report(),suppliers=SupplierApi.Report(),supplierDebug=DebugSupplier.LastReport,phone=PhoneApi.Report(),phoneInteractive=DebugPhone.InteractiveReport,dialogue=DialogueApi.Report(),dialogueDebug=DebugDialogue.LastReport,dialogueInteractive=DebugDialogue.InteractiveReport,reading=ReadingApi.Report(),readingDebug=DebugReading.LastReport,readingLifecycleDebug=DebugReading.LifecycleReport,delegateBridge=NativeDelegateBridge.Report(),packs=ContentPackLoader.Report(),items=ItemRegistry.Report(),activation=ItemActivation.Report(),lifecycle=GameEvents.Report(),forgeLifecycle=LifecycleApi.Report(),modules=ModuleApi.Report(),contentApplication=ContentApi.Report(),effects=EffectApi.Report(),presentation=PresentationApi.Report(),loot=LootTables.Report(),inventoryGuards=InventoryRuntime.Report(),


                    externalPower=ExternalPowerApi.Report(),externalPowerDebug=DebugExternalPower.LastReport,powerDebug=DebugPower.LastReport,nativeFurnaceDebug=DebugNativeFurnace.LastReport,saveReadbackDebug=DebugSaveReadback.LastReport,manufacturingTerminal=ManufacturingTerminal.Snapshot(),manufacturingTerminalDebug=DebugManufacturingTerminal.LastReport,machinePersistenceDebug=DebugMachinePersistence.LastReport,machinePersistenceReplay=DebugMachinePersistence.ReplayReport,machineNightDebug=DebugMachineNight.LastReport,machineAutomationDebug=DebugMachineAutomation.LastReport,machineAutomation=MachineAutomationApi.Report(),machineRuntime=MachineRuntimeApi.Report(),machineRegistration=MachineRegistrationApi.Report(),machineRegistrationCatalog=MachineRegistrationApi.Snapshot(),machineRegistrationDebug=DebugMachineRegistration.LastReport,machineMaterialDebug=DebugMachineMaterials.LastReport,machineRecipes=MachineRecipeApi.Report(),machineRecipeCatalog=MachineRecipeApi.Snapshot(),machineRecipeDebug=DebugMachineRecipes.LastReport,machineTransactions=MachineTransactionApi.Report(),machineTransactionDebug=DebugMachineTransactions.LastReport,machineContainerConditions=MachineContainerConditionApi.Snapshot(),machineLayouts=MachineLayoutApi.Report(),machineLayoutDebug=DebugMachineLayouts.LastReport,recipeGuide=RecipeGuideBrowser.Report(),recipeGuideCatalog=RecipeGuideApi.Snapshot(),npcStock=NpcStockRuntime.Report(),npcStockDebug=DebugNpcStock.LastReport,itemStock=ItemStockRuntime.Report(),itemStockDebug=DebugItemStock.LastReport,nativeFactories=ItemApi.FactorySnapshot(),nativeFactoryDebug=DebugNativeItems.LastReport,modulesDebug=DebugModules.LastReport,liquidTransferDebug=DebugLiquidTransfer.LastReport,batchDebug=DebugBatch.Report(),lifecycleTrace=DebugLifecycle.Report(),productionDebug=DebugProduction.LastReport,runDataDebug=DebugRunData.LastReport,lootDebug=DebugLoot.LastReport,localizationDebug=DebugLocalization.LastReport,guideLocalizationDebug=DebugGuideLocalization.LastReport,itemInspectionDebug=DebugItemInspection.LastReport,resourcesDebug=DebugResources.LastReport,inventoryDebug=DebugInventory.LastReport,freezeDebug=DebugInventory.LastFreezeReport,errors=Errors,nativeChecks=ItemDiagnostics.LastReport},new JsonSerializerOptions{WriteIndented=true}));


            return true;


        }catch(Exception e){MelonLogger.Error("[EZapi] Diagnostics: "+e); return false;}


    }


    public override void OnDeinitializeMelon()


    {


        NativeUiPreviewApi.Close();WildNetworkBrowser.Stop();DebugSaveGraph.Clear();DebugRegistration.Clear();RegistrationApi.Stop();


        DepartureApi.Stop();ClientVisitApi.Stop();SupplierApi.Stop();DebugSupplier.Clear();StartApi.Stop();DebugWorkshop.Clear();WorkshopApi.Stop();PresentationApi.Stop();


        _ready=false; NpcStockRuntime.Stop();ItemStockRuntime.Stop();DebugNpcStock.Clear();DebugItemStock.Clear();MachineRuntimeApi.Stop();MachineAutomationApi.Stop();MachineRegistrationApi.Stop();MachineRecipeApi.Stop();MachineTransactionApi.Stop();RecipeGuideApi.Stop();MachineLayoutApi.Stop();DebugMachineLayouts.Clear();DebugMachineTransactions.Clear();DebugMachineRecipes.Clear();DebugMachineMaterials.Clear();DebugMachineRegistration.Clear();DebugMachineAutomation.Clear();DebugMachineNight.Clear();DebugMachinePersistence.Clear();DebugManufacturingTerminal.Clear();DebugExternalPower.Clear();DebugPower.Clear();DebugNativeFurnace.Clear();DebugSaveReadback.Clear();DebugReading.Clear();DebugDialogue.Clear();DebugPhone.Clear();DebugRunPurchase.Clear();_debug.Stop();DebugSpriteAssets.Clear();SpriteAssets.Clear();DebugNativeItems.Clear();DebugLiquidTransfer.Clear();DebugBatch.Clear();DebugLifecycle.Clear();DebugProduction.Clear();DebugLocalization.Clear();DebugItemInspection.Clear();DebugResources.Clear();DebugInventory.Clear();LootTables.Stop();InventoryEndpointRuntime.Stop();ExternalPowerApi.Stop();InventoryRuntime.Stop();ModuleApi.Stop();ReadingApi.Stop();DialogueApi.Stop();PhoneApi.Stop();LifecycleApi.Stop();EffectApi.Stop();ContentApi.Stop();GameEvents.Stop();CompatibilityPatches.Stop();DeclarativeItemLocalization.Stop();ItemRegistry.Stop();NativeDelegateBridge.Stop();LegacySpriteAdapter.Clear();ContentPackLoader.Clear();Errors.Clear();DeveloperToolsSettings.Reset();DebugRegressionSession.Reset();


    }





    public override void OnGUI() { if(_ready){
        // A blocked-save notice belongs to the runtime, even without developer tools.
        if(InventoryEndpointApi.SaveError is { } saveError)UnityEngine.GUI.Box(new UnityEngine.Rect(730,60,UnityEngine.Mathf.Max(300,UnityEngine.Screen.width-750),90),"保存被阻止，请勿退出 / Save blocked: "+saveError);
        if(DeveloperToolsSettings.Current.Enabled)_debug.Draw();RecipeGuideBrowser.Draw();WorkshopBrowser.Draw();} }


    public override void OnUpdate() { if(_ready){


        if(_storeReadyPending&&VisibleStoreInventory.IsReady)


            _storeReadyPending=!GameSession.ObserveStoreReady();


        NativeUiPreviewApi.Maintain();WildNetworkBrowser.Maintain();DepartureRuntime.Maintain();ReadingApi.Maintain();DialogueApi.Maintain();ClientVisitApi.Maintain();SupplierApi.Maintain();PhoneApi.Maintain();NativeFurnacePower.Maintain();EffectApi.Maintain();RecipeGuideBrowser.Update();WorkshopBrowser.Update();
        if(DeveloperToolsSettings.Current.Enabled){DebugInventory.Update();_debug.Update();}
        if(DeveloperToolsSettings.Current.RegressionFixtures){DebugSupplier.Maintain();DebugDialogue.Update();DebugPhone.Update();DebugReading.Update();DebugGuideLocalization.Update();DebugBatch.Update();}
        } }


    public override void OnSceneWasLoaded(int buildIndex, string sceneName)


    { if(_ready) _storeReadyPending=false; }


    public override void OnSceneWasInitialized(int buildIndex,string sceneName)


    {if(_ready)_storeReadyPending=true;}


    public override void OnSceneWasUnloaded(int buildIndex,string sceneName)


    {


        if(!_ready)return;


        NativeUiPreviewApi.Close();WildNetworkBrowser.SceneChanged();DepartureRuntime.SceneChanged();ReadingApi.SceneChanged();SupplierApi.SceneChanged();DebugSupplier.SceneChanged();ClientVisitApi.SceneChanged();DialogueApi.SceneChanged();PhoneApi.SceneChanged();RecipeGuideBrowser.Close();WorkshopBrowser.SceneChanged();


        // 在旧场景卸载时关闭掉落入口；新场景 Awake 中的官方加载事件可能早于 OnSceneWasLoaded，不能事后抹掉它。


        _storeReadyPending=false;LootTables.SceneChanged();GameSession.Invalidate();ModuleApi.SceneChanged();DebugLiquidTransfer.Clear();DebugBatch.SceneChanged();InventoryRuntime.SceneChanged();DebugRunData.Clear();DebugLoot.Clear();DebugItemInspection.Clear();DebugResources.Clear();DebugInventory.Clear();_debug.SceneChanged();


    }


}





