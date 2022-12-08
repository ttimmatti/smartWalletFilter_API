using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;
using CsvHelper;
using Newtonsoft.Json;
using System.Net.Http;
using DotNetEnv;

class Program
{
    public static void Main()
    {
        DotNetEnv.Env.Load();

        string ftm_api = Environment.GetEnvironmentVariable("FTM_API");


        bool development = true;
        if (!development)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            WebApplication app = builder.Build();

            app.UseHttpsRedirection();
            app.UseSwagger();
            app.UseSwaggerUI();
            

            app = Maps.MapPost(app);

            app.Run();
        }

        //SmartWallets.Get(File.ReadAllText("here.csv"), "10000", "5", "1000", "");

        HttpClient httpClient = new HttpClient();

        while(true)
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage();
            requestMessage.RequestUri = new Uri("");


        }

    }
}

class Maps
{
    public static WebApplication MapPost(WebApplication app)
    {
        //?balance=10000&txs=5&minswap=1000&token=<token address>
        app.MapPut("/upload",
            async (HttpRequest request, string? balance, string? txs, string? minswap, string? token) =>
            {
                string fileContent = "";
                using (var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8))
                {
                    // Read the raw file as a `string`.
                    fileContent = await reader.ReadToEndAsync();
                }    
                // Do something with `fileContent`...
                SmartWallets.Get(fileContent, balance, txs, minswap, token);
        
                //return "File Was Processed Sucessfully!";
                return SmartWallets.Get(fileContent, balance, txs, minswap, token);
            }
        ).Accepts<IFormFile>("json"); //NEED TO CLARIFY HOW THIS WORKS

        return app;
    }
}

class SmartWallets
{
    public static string Get(string csv, string balance, string numOfTxs, string minSwap, string token)
    {
        //if value is null then we assign it its default value
        balance = balance??"10000"; numOfTxs = numOfTxs??"5"; minSwap = minSwap??"1000"; token = token??"";

        //csv to list of TXs
        IList<Tx> list = Data.CsvToListOfTXs(csv);
        //filter for duplicates by txhash
        list = list.GroupBy(el => el.Txhash).Select(el => el.First()).ToList<Tx>();

        /*
        1. Swap value > <minswap|1000$>
        If true:
            Check that the wallet is active: 
            1.1 — Balance is > <balance|10000>$ in all chains. 
            1.2 — TX history is > <txs|5> transactions for the last 7 days.
        */

        //ISSUES
        //to determine which network is it, the bot takes first tx hash and searches for the transaction
        //in all of the supported networks.

        //determine the network
        //What if the Api is down!!!

        //1. SWAP VALUE > minSwap|1000$
        //
        //

        //
        //
        //


        //File.WriteAllText("here.json", JsonConvert.SerializeObject(list, Formatting.Indented));

        //Tx tx = list.ElementAt(0);
        
        return ($"balance={balance}&txs={numOfTxs}&minswap={minSwap}&token={token}");
    }

    
}

class Data
{
    public static IList<Tx> CsvToListOfTXs(string text)
    {
        var reader = new StringReader(text);
        var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);

        var records = csv.GetRecords<Tx>().ToList<Tx>();

        return records;
    }
}

class Tx
{
    public string Txhash { get; set; }
    public string UnixTimestamp { get; set; }
    public string DateTime { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public string TokenValue { get; set; }
    public string USDValueDayOfTx { get; set; }
    public string ContractAddress { get; set; }
    public string TokenName { get; set; }
    public string TokenSymbol { get; set; }
}

//"Txhash","UnixTimestamp","DateTime","From","To","TokenValue","USDValueDayOfTx","ContractAddress","TokenName","TokenSymbol"