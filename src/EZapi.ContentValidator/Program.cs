using System.Text.Json;
using EZapi;
const string usage="Usage: ezapi-validate items.json | --registration batch.json | --loot loot.json | --pack folder | --packs root | --localization LocalizationFolder | --machine-layout layout.json | --recipe-guide owner.id recipes.json | --requirements requirements.json | --read-save file.es3 | --capabilities | --help";
if(args.Length==1&&args[0] is "--help" or "-h") {Console.WriteLine(usage);return 0;}
var validArguments=args.Length==1&&(!args[0].StartsWith("-",StringComparison.Ordinal)||args[0]=="--capabilities")
    ||args.Length==2&&args[0] is "--registration" or "--loot" or "--pack" or "--packs" or "--localization" or "--machine-layout" or "--requirements" or "--read-save"
    ||args.Length==3&&args[0]=="--recipe-guide";
if(!validArguments) {
    Console.Error.WriteLine(usage);
    Console.WriteLine(JsonSerializer.Serialize(new{valid=false,error="EZAPI_CLI_ARGUMENTS: unknown command or wrong argument count; use --help."}));return 2;
}
try {
    if(args.Length==2&&args[0]=="--requirements") {
        using var requirementFile=File.OpenRead(args[1]);var definition=Requirements.Parse(requirementFile);
        int Count(RequirementDefinition node)=>1+node.Children.Sum(Count);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,kind="requirements",nativeValidationPerformed=false,
            evaluationPerformed=false,nodeCount=Count(definition),definition,
            scope="strict declaration validation only; native target existence and current eligibility require RequirementApi.Evaluate in the loaded game"},
            new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull}));return 0;
    }
    if(args.Length==2&&args[0]=="--registration") {
        using var registrationFile=File.OpenRead(args[1]);var batch=JsonRegistration.Parse(registrationFile);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,kind="registration",batch.OwnerId,batch.Version,dependencyResolutionPerformed=false,nativeValidationPerformed=false,scope="single metadata declaration shape; cross-batch dependency resolution requires runtime registration"}));return 0;
    }
    if(args.Length==2&&args[0]=="--read-save") {
        using var read=SaveReadbackApi.ReadFile(args[1]);var valid=read.Status==SaveReadStatus.Readable;
        Console.WriteLine(JsonSerializer.Serialize(new{valid,kind="save-readback",status=read.Status.ToString(),read.ByteCount,read.Reason,
            rootKind=valid?read.Root.ValueKind.ToString():null,scope="read-only text snapshot; no game restore or writes"}));return valid?0:1;
    }
    if(args.Length==2&&args[0]=="--machine-layout") {
        using var layoutFile=File.OpenRead(args[1]);var definition=JsonMachineLayout.Parse(layoutFile);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,kind="machine-layout",nativeValidationPerformed=false,definition}));return 0;
    }
    if(args.Length==3&&args[0]=="--recipe-guide") {
        using var guideFile=File.OpenRead(args[2]);var recipes=JsonRecipeGuide.Parse(args[1],guideFile);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,kind="recipe-guide",nativeValidationPerformed=false,count=recipes.Count,recipes}));return 0;
    }
    if(args.Length==2&&args[0]=="--localization") {
        if(!Directory.Exists(args[1]))throw new DirectoryNotFoundException("EZAPI_LOCALIZATION_PATH: directory does not exist: "+args[1]);
        var catalog=Localization.LoadDirectory(args[1]);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,kind="localization",nativeValidationPerformed=false,index=catalog.Index}));return 0;
    }
    if(args.Length==1&&args[0]=="--capabilities") {
        Console.WriteLine(JsonSerializer.Serialize(new{version=FrameworkInfo.Version,jsonProtocolVersion=FrameworkInfo.JsonProtocolVersion,
            capabilities=FrameworkInfo.GetCapabilities()}));return 0;
    }
    if(args.Length==2&&args[0]=="--pack") {
        var result=ContentPacks.Check(args[1]);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=result.Valid,kind="pack",dependencyResolutionPerformed=false,
            nativeValidationPerformed=false,pack=result}));return result.Valid?0:1;
    }
    if(args.Length==2&&args[0]=="--packs") {
        var result=ContentPacks.CheckDirectory(args[1]);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=result.Valid,kind="packs",dependencyResolutionPerformed=true,
            nativeValidationPerformed=false,packs=result.Packs,loadOrder=result.LoadOrder}));return result.Valid?0:1;
    }
    if(args.Length==2) {
        using var json=File.OpenRead(args[1]);var loot=JsonLoot.Parse(json);
        Console.WriteLine(JsonSerializer.Serialize(new{valid=true,kind="loot",tables=loot.Tables.Count,groups=loot.Groups.Count,
            patches=loot.Patches.Count,operationCount=JsonLoot.OperationCount(loot),definition=loot}));return 0;
    }
    using var file=File.OpenRead(args[0]);var items=JsonItems.Parse(file);
    Console.WriteLine(JsonSerializer.Serialize(new{valid=true,count=items.Count,items=items.Select(i=>new{i.Id,i.Name,i.Value,i.Width,i.Height,
        i.LocalizedName,i.LocalizedDescription,i.LocalizedShortDescription,i.LocalizedLongDescription,i.LocalizedFlavorText,i.LocalizedCustomText,
        i.ShortDescription,i.LongDescription,i.FlavorText,i.CustomText,i.DescriptionPlacement,i.Types,i.TypeNames,i.Tags,i.ShapeRows,
        i.InitialCount,i.MaxStackSize,i.ContrabandLevel,i.UseCount,i.Food,i.LiquidContainer,i.Container,i.Activation,i.Sounds,i.Tooltip})}));
    return 0;
}catch(Exception e){Console.WriteLine(JsonSerializer.Serialize(new{valid=false,error=e.Message}));return 1;}
