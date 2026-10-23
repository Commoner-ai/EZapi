using EZapi;
namespace Author.Guild;

/// <summary>Call Start after EZapi initialization and Stop before this consumer unloads. No native assemblies needed in source.</summary>
public static class Factions
{
    private const string Owner="author.guild", Id="author.guild.free_traders";
    public static PresentationResult Start()=>FactionUiApi.Register(Owner,
        new FactionUiDefinition(Id,new("自由商会","Free traders")){Color="#71546D"},context=>{
            var data=RunData.Open(Owner); // fresh handle each read; do not mutate here
            return new FactionReputationView(data.GetDouble("reputation",0)){
                Perks=new[]{new FactionPerkView("author.guild.discount",new("商会优惠","Guild discount"),
                    new("商会商人给予交易优惠。","Guild traders offer a discount."),60,data.GetBool("discount-active")){Icon="star"}}
            };
        });
    public static PresentationResult Stop()=>FactionUiApi.Unregister(Owner,Id);
}
