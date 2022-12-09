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

        SmartWallets.Get(File.ReadAllText("here.csv"), 1000, 5, 1000, "");
    }
}

class Maps
{
    public static WebApplication MapPost(WebApplication app)
    {
        //?balance=10000&txs=5&minswap=1000&token=<token address>
        app.MapPut("/upload",
            async (HttpRequest request, int? balance, int? txs, int? minswap, string? token) =>
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
    public static string Get(string csv, int? balance, int? numOfTxs, int? minSwap, string token)
    {
        //if value is null then we assign it its default value
        balance = balance??10000; numOfTxs = numOfTxs??5; minSwap = minSwap??1000; token = token??"";

        //load networks

        //csv to list of TXs
        IList<Tx> list = Data.CsvToListOfTXs(csv);
        //filter for duplicates by txhash
        //list = list.GroupBy(el => el.Txhash).Select(el => el.First()).ToList<Tx>();

        /*!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        0. Determine the network of TXs

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
        Explorer.DetermineNetwork(list);

        //1. SWAP VALUE > minSwap|1000$
        // check for stables vs 1000
        // need to hardcode:
        // chain native tokens(5) and wrapped(5) and natives in the network, stables(usdt,busd,dai,usdn etc)
        //
        //list = list.Where(x => double.Parse(x.USDValueDayOfTx) >= minSwap).ToList<Tx>();
        IList<string> walletsList = new List<string>();

        // int j = 0;
        // for (int i = 0; i < list.GroupBy(x => x.ContractAddress).Count(); i++)
        // {
        //     while (list.ElementAt(j).ContractAddress == list.ElementAt(i).ContractAddress)
        //     {
        //         //if ()

        //         j++;
        //     }
        // }
        //
        //
        //



        File.WriteAllText("here.json", JsonConvert.SerializeObject(list, Formatting.Indented));

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

        for (int i = 0; i < records.Count(); i++)
        {
            string txUsd = records.ElementAt(i).USDValueDayOfTx;
            if (txUsd.IndexOf(",") != -1)
                records.ElementAt(i).USDValueDayOfTx = txUsd.Remove(txUsd.IndexOf(","), 1);
        }

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

class Stables
{
    // public static string[] All(string network)
    // {

    // }
}

class Network
{
    public string Name { get; set; }
    public string Api { get; set; }
    public string Key { get; set; }
    public static IList<Network> AllNetworks()
    {
        return JsonConvert.DeserializeObject<List<Network>>(File.ReadAllText("networks.json"));
    }
    
}

class Explorer
{
    public static string DetermineNetwork(IList<Tx> listOfTxs)
    {
        IList<Network> listOfNetworks = Network.AllNetworks();

        HttpClient httpClient = new HttpClient();
        
        //while? there still is a second variant(network)
        for (int i = 0; i < 100; i++)
        {
            Tx tx = listOfTxs.ElementAt(i);

            Console.WriteLine("".PadRight(5, '#'));

            for (int j = 0; j < listOfNetworks.Count(); j++)
            {
                Network network = listOfNetworks.First(x => x.Name == "Polygon");

                string status = GetResponseAsync(httpClient, network, tx).Result;

                Console.WriteLine($"{j}Result for upper:" + status);
            }
        }

        return "2";
    }

    private static async Task<string> GetResponseAsync(HttpClient httpClient, Network network, Tx tx)
    {
        TxReceiptResponse receiptResponse = null;
        for (int i = 0; i < 2; i++)
        {
            bool success = true;
            try
            {       
                HttpRequestMessage message = new HttpRequestMessage();
                message.RequestUri = new Uri(network.Api + "?module=transaction&action=gettxreceiptstatus&txhash=" +
                tx.Txhash + "&apikey=" + network.Key);

                var response = await httpClient.SendAsync(message);

                var responseReader = response.Content.ReadAsStringAsync().Result;
                receiptResponse = JsonConvert.DeserializeObject<TxReceiptResponse>(responseReader);
            }
            catch (Exception exc)
            {
                Thread.Sleep(3000);

                success = false;
                //SEND MESSAGE OR SMTHING //NEEDS TO BE REFACTORED LATER
            }

            if (success)
                break;
        }
        
        if (receiptResponse == null)
        {
            //also can be a message, to control availability
            return "failed";
        }

        return receiptResponse.result.status;
    }

    private class TxReceiptResponse
    {
        public string status { get; set; }
        public string message { get; set; }
        public Result result { get; set; }
    }
    private class Result
    {
        public string status { get; set; }
    }
}