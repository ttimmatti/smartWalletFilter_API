#smart wallet filter

it's an api that i've created for my other project to filter smart wallets from a Transaction list

smart wallet - definition used to classify users who bought before the pump and etc. (made a smart move :) )

i am currently hosting it on my server
  request looks like so, request method POST
  ip:port/upload?balance=<balance>&txs=<txs>&minswap=<minswap>&token=<token address>&buyonly=<bool>

it accepts a csv file that represents a list of txs on one of the supported chains (eth, polygon, bsc, arbitrum, optimism) and a set of parameters,
  such as balance(minimal balance sum on 5 chains for a wallet to classify as a "smart wallet"), txs(minimal number of txs in the last week, that a wallet is still active),
  minswap(transactions with USD$ value of less that minimal swap are filtered out), token(token address of a token that the csv snapshot was made of)
  
csv snapshot can be from the network explorer, e.g. etherscan. a snapshot must be made with token filter

all the parameters in api are optional, they have default values, you can view them in code.
all the programm logic is also described in the code itself, feel free to explore.
