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

        bool development = false;
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

        SmartWallets.Get(File.ReadAllText("here.csv"), 10000, 5, 1000, "", "true");
    }
}

class Maps
{
    public static WebApplication MapPost(WebApplication app)
    {
        //?balance=10000&txs=5&minswap=1000&token=<token address>
        app.MapPut("/upload",
            async (HttpRequest request, int? balance, int? txs, int? minswap, string? token, string? buyonly) =>
            {
                string fileContent = "";
                using (var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8))
                {
                    // Read the raw file as a `string`.
                    fileContent = await reader.ReadToEndAsync();
                }    
                // Do something with `fileContent`...
                SmartWallets.Get(fileContent, balance, txs, minswap, token, buyonly);
        
                //return "File Was Processed Sucessfully!";
                return SmartWallets.Get(fileContent, balance, txs, minswap, token, buyonly);
            }
        ); //.Accepts<IFormFile>("json"); //NEED TO CLARIFY HOW THIS WORKS

        return app;
    }
}

class SmartWallets
{
    public static string Get(string csv, int? balance, int? numOfTxs, int? minSwap, string token, string buyonly)
    {
        //if value is null then we assign it its default value
        balance = balance??10000; numOfTxs = numOfTxs??5; minSwap = minSwap??1000; token = token??"";
        buyonly = buyonly??"false";

        //load networks

        //csv to list of TXs
        List<Tx> list = Data.CsvToListOfTXs(csv);
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

        string network = Explorer.DetermineNetwork(list);
        Console.WriteLine(network); //REMOVE FOR PRODUCTION!!!!!!!!!!!!!!!!!!!!!!!

        //1. SWAP VALUE > minSwap|1000$
        // check for stables vs 1000
        // need to hardcode:
        // chain native tokens(5) and wrapped(5) and natives in the network, stables(usdt,busd,dai,usdn etc)
        //
        list = Data.FilterTxWithLessThanMinswap(list, network, minSwap);
        //now we need to implement solution for
        //1. buyonly included
        //2. tokenfilter included

        //buyonly section: to filter buyonly txs we need to be sure that in the last action
        // of the trnsaction the sender of the transaction receives That token, which we are working with
        //        
        //!!! So, if buyonly is true, than we also need tokenContract to be specified.
        // Or we can try to guess it, based on the statistics...
        if (buyonly == "true")
        {
            //first we need to find out which token are we working with
            // my plan is to choose a token that is not a stable and is
            // met most frequently +
            string tokenContract = Data.DetermineTokenContract(list, network);

            list = Data.FilterBuyOnly(list, tokenContract);
        }
        //      IList<string> walletsList = new List<string>();

        
        
        
        



        File.WriteAllText("here.json", JsonConvert.SerializeObject(list, Formatting.Indented));

        //Tx tx = list.ElementAt(0);
        
        return ($"balance={balance}&txs={numOfTxs}&minswap={minSwap}&token={token}");
    }
}



class Data
{
    public static List<Tx> CsvToListOfTXs(string text)
    {
        var reader = new StringReader(text);
        var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);

        var records = csv.GetRecords<Tx>().ToList<Tx>();

        for (int i = 0; i < records.Count(); i++)
        {
            string txTokenValue = records.ElementAt(i).TokenValue;
            if (txTokenValue.IndexOf(",") != -1)
                records.ElementAt(i).TokenValue = txTokenValue.Remove(txTokenValue.IndexOf(","), 1);
        }

        return records;
    }

    public static List<Tx> FilterTxWithLessThanMinswap(List<Tx> list, string networkForStables,
            int? minSwap)
    {
        string[] stablesContractsInTheNetwork = AllStablesInNetworks.ArrayContractsInNetwork(networkForStables);

        for (int i = 0; i < list.Count();)
        {
            Tx tx = list.ElementAt(i);

            bool includesStableSwap = false;
            bool stableSwapMoreThanMinswap = false;
            int j = i;

            while (list.ElementAt(j).Txhash == tx.Txhash)
            {
                if (stablesContractsInTheNetwork.Any(list.ElementAt(j).ContractAddress.ToLower().Contains))
                {
                    includesStableSwap = true;

                    if (double.Parse(list.ElementAt(j).TokenValue) >= minSwap)
                    {
                        stableSwapMoreThanMinswap = true;
                    }
                }

                j++;
                if (j >= list.Count())
                    break;
            }

            if (!stableSwapMoreThanMinswap)
            {
                j -= list.RemoveAll(x => x.Txhash == tx.Txhash);
            }

            i = j;
        }

        return list;
    }

    
    public static string DetermineTokenContract(List<Tx> list, string networkForStables)
    {
        //this method can also do some statistics on whether the token
        // get chosen the proper way

        string[] stablesContractsInTheNetwork = AllStablesInNetworks.ArrayContractsInNetwork(networkForStables);

        IList<TokenContractFilter> listCounter = new List<TokenContractFilter>();

        for (int i = 0; i < list.Count(); i++)
        {
            Tx tx = list.ElementAt(i);

            int indexOfContract = -1;
            //if this contract is already on the list, take its index. otherwise -1
            if (listCounter.Any(token => token.contract.Equals(list.ElementAt(i).ContractAddress,
                    StringComparison.OrdinalIgnoreCase)))
            {
                indexOfContract = listCounter.IndexOf(listCounter.First(x => x.contract.Equals(list.ElementAt(i).ContractAddress,
                StringComparison.OrdinalIgnoreCase)));
            }

            if (indexOfContract != -1)
            {
                listCounter.ElementAt(indexOfContract).counter++;
            }
            else if (!stablesContractsInTheNetwork.Any(list.ElementAt(i).ContractAddress.ToLower().Contains))
            {
                listCounter.Add(new TokenContractFilter(){
                    name = tx.TokenName,
                    contract = tx.ContractAddress,
                    counter = 1
                    });
            }
        }

        string mostRepeatedToken_Contract = "";
        for (int i = 0; i < listCounter.Count(); i++)
        {
            if (!listCounter.Any(token => token.counter > listCounter.ElementAt(i).counter))
            {
                mostRepeatedToken_Contract = listCounter.ElementAt(i).contract;
            }
        }

        return mostRepeatedToken_Contract;
    }

    private class TokenContractFilter
    {
        public string name { get; set; }
        public string contract { get; set; }
        public int counter { get; set; }
    }

    public static List<Tx> FilterBuyOnly(List<Tx> list, string tokenContract)
    {
        List<SmartWallet> walletObj = new List<SmartWallet>();

        for (int i = 0; i < list.Count();)
        {
            Tx tx = list.ElementAt(i);

            if (list.ElementAt(i+1).Txhash != tx.Txhash)
            {
                //if it is a "buy the specified token tx", than it's last action would be
                // to transfer the specified token to contract initiator address(buyer)
                //SO, we can save the address of the receiver as the buyer's address
                if (tx.ContractAddress == tokenContract)
                {
                    //What if the wallet has two Txs. Duplicates is bullshit...
                }
            
            }
        }

        return list; //#################################
    }
}

class SmartWallet
{
    public string walletAddress { get; set; }
    public string balance { get; set; }
    public string remainingTokenBalance { get; set; }
    public string PNL { get; set; }
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

class AllStablesInNetworks
{
    public string network { get; set; }
    public IList<Stable> stables { get; set; }
    public class Stable
    {
        public string name { get; set; }
        public string contract { get; set; }
    }
    
    //returns list of stables in the passed in network($name, $contract)
    public static IList<Stable> ListAllInNetwork(string network)
    {
        IList<AllStablesInNetworks> list = JsonConvert.DeserializeObject<IList<AllStablesInNetworks>>(
            File.ReadAllText("stables.json")
        );

        IList<Stable> listOfStables = list.First(x => x.network == network).stables;

        return listOfStables;
    }

    //returns array of strings that consists only the contract addresses of stables
    // in the passed in network
    public static string[] ArrayContractsInNetwork(string network)
    {
        IList<Stable> list = ListAllInNetwork(network);

        string[] array = new string[list.Count()];

        for (int i = 0; i < array.Length; i++)
        {
            array[i] = list.ElementAt(i).contract.ToLower();
        }

        return array;
    }
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

        string lastNetwork = "";
        
        //while? there still is a second variant(network)
        for (int i = 0; i < 3; i++)
        {
            Tx tx = listOfTxs.ElementAt(i);

            for (int j = 0; j < listOfNetworks.Count(); j++)
            {
                Network network = listOfNetworks.First(x => x.Name == "Polygon");

                string status = GetResponseAsync(httpClient, network, tx).Result;
                
                if (status == "")
                {
                    listOfNetworks = listOfNetworks.Where(x => x.Name != network.Name).ToList<Network>();
                }
                else if (status == "1" || status == "0")
                {
                    lastNetwork = network.Name;
                }
            }

            if (listOfNetworks.Count() < 2)
                break;
        }

        return lastNetwork;
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
            //also can be a message, to control errorFlow of the api
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

