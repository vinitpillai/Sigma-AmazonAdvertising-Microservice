module Sigma.Common.RabbitMQ

open System
open System.Text
open System.Linq
open System.Threading
open System.Collections.Generic
open RabbitMQ.Client
open RabbitMQ.Client.Events
open Newtonsoft.Json

(* Create a connection factory using a cluster ip, which is likely to be the loadbalancer *)
let createConnectionFactory clusterIP =  
    let factory = new ConnectionFactory();
    factory.Uri <- "amqp://" + clusterIP;
    factory.SocketReadTimeout <- 1000
    factory.SocketWriteTimeout <- 1000
    factory
    
(** Send a payload to the named exchange using an optional routing key, via a connection and channel **)
let post exchange (routingKey: string option) (connection: IConnection) (channel: IModel) (replyTo: string option) (headers: Dictionary<string, obj> option) (payloadText: string) = 

    // If there is no routingKey the exchange type is Fanout, otherwise. Topic
    let exchangeType = if routingKey.IsSome then ExchangeType.Topic else ExchangeType.Fanout
    
    // Set up the channel
    if exchange <> "" then channel.ExchangeDeclare(exchange, exchangeType, false)
                
    // Convert the psyload into a byte[]
    let payload = Encoding.ASCII.GetBytes(payloadText)
                
    // Headers (record the publish date / time)
    let basicProperties = channel.CreateBasicProperties();

    // Disctionary for headers
    basicProperties.Headers <- new Dictionary<string, obj>();

    // Add publishedAt header
    basicProperties.Headers.Add("publishedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"))

    // Did we get any headers to attach?
    if headers.IsSome then headers.Value.ToList() |> List.ofSeq |> List.iter (fun toAdd -> basicProperties.Headers.Add( toAdd.Key, toAdd.Value ) )

    // If a reply path is specified, provide it in the message
    if replyTo.IsSome then basicProperties.ReplyTo <- replyTo.Value
                
    // Publish
    channel.BasicPublish ( exchange , ( match routingKey.IsSome with | true -> routingKey.Value | false -> "" ) , basicProperties , payload )

(** Connect to an exchange and post **)
let connectAndPost exchange (routingKey: string option) (connectionFactory: ConnectionFactory) (replyTo: string option) (headers: Dictionary<string, obj> option) (payloadText: string) =

    use connection = connectionFactory.CreateConnection()
    (
        use channel = connection.CreateModel()
        (
            post exchange routingKey connection channel replyTo headers payloadText 
        )
    )

(** Boilerplate to bind queue **)
let bindQueue routingKey exchange (exchangeType: string) callback (connection: IConnection) (channel: IModel) timeout (listening: bool ref) (killSwitch: bool ref) = 
    
    async {

        try
        
            // Declare the exchange, unless this is the default exchange we're dealing with
            if exchange <> "" then channel.ExchangeDeclare(exchange, exchangeType, false)
                                            
            // Set up the queue
            let queueName = channel.QueueDeclare(routingKey, false, false, true, null).QueueName

            // Create the consumer
            let consumer = new EventingBasicConsumer(channel)
                
            // Attach message recieved callback
            consumer.Received.Add callback

            // Bind to queues
            let consumerTag = channel.BasicConsume(queueName, true, consumer);
                
            // Bind routes
            if exchange <> "" then channel.QueueBind(queueName.ToString(), exchange, queueName);
                
            // Log
            String.Format("{1}: Listening on: {0}", queueName, DateTime.Now.ToLongTimeString()) |> Console.WriteLine

            // Update ref to show that we're listening
            listening := true

            // We need to keep execution in this block so that messages can be consumed.
            let rec keepOpen count = 
                match count with | x when x > timeout || !killSwitch -> ()
                                 | _ -> async { do! Async.Sleep (1000) } |> Async.RunSynchronously ; keepOpen (count + 1)
        
            // Wait for timeout or for the kill switch to be activated
            keepOpen 0

            // Exiting
            String.Format("{1}: Finished listening on: {0}", queueName, DateTime.Now.ToLongTimeString()) |> Console.WriteLine

            // Done listening
            listening := false

        with ex -> String.Format("{1}: BindQueue exception: {0}", ex.Message, DateTime.Now.ToLongTimeString()) |> Console.WriteLine
    }

(** listen on the default echnage for messages on a named queue and trigger a callback when recieved **)
let simpleListen queueName exchange exchangeType callback (connectionFactory: ConnectionFactory) timeout (listening: bool ref) (killSwitch: bool ref)  = 
    
    try

        // Automatic recovery
        connectionFactory.AutomaticRecoveryEnabled <- true

        // Log
        String.Format("{1}: Preparing to open connection to queue: {0}", queueName, DateTime.Now.ToLongTimeString()) |> Console.WriteLine

        // Create the connection
        use connection = connectionFactory.CreateConnection()
        (
            use channel = connection.CreateModel()
            (
                bindQueue queueName exchange exchangeType callback connection channel timeout listening killSwitch |> Async.RunSynchronously
            )     
        )
        
    with ex -> String.Format("{1}: Exception opening queue: {0}", ex.Message, DateTime.Now.ToLongTimeString()) |> Console.WriteLine

(* Send a message and wait for responses using RabbitMQs direct reply feature *)
let directReply (routingKey: string option) exchange (responses: 'b list ref) (connectionFactory: ConnectionFactory) timeoutSecs responseThreshold (request: 'a) = 
     
     // The reply to channel
     let rabbitMQReplyChannel = "amq.rabbitmq.reply-to"

     // Sleep interval for checking statuses
     let asyncSleepIntervalMilliseconds = 50

     async {
            
        try

            // Automatic recovery
            connectionFactory.AutomaticRecoveryEnabled <- true

            // Note
            String.Format("{1}: Preparing to open connection to queue: {0}", rabbitMQReplyChannel, DateTime.Now.ToLongTimeString()) |> Console.WriteLine

            // Create the connection
            use connection = connectionFactory.CreateConnection()
           
            // Create the channel
            use channel = connection.CreateModel()
            
            // Capture responses and add to responses list   
            let callback (args: BasicDeliverEventArgs) =
                let incomming = Encoding.ASCII.GetString(args.Body)
                responses := JsonConvert.DeserializeObject<'b>(incomming) :: !responses 
            
            // Are we currently listening?
            let listening = ref false 

            // Kill switch
            let killSwitch = ref false

            // Bind queue
            bindQueue rabbitMQReplyChannel "" ExchangeType.Fanout callback connection channel timeoutSecs listening killSwitch |> Async.Start

            // Timeout cancellation token
            let cancellationSource = new CancellationTokenSource()

            // Have we timed out?
            let timedOut = ref false 

            // Set our time out timer running
            let timoutHandler = 
                async { 
                    do! Async.Sleep (timeoutSecs * 1000)
                    timedOut := true 
                } 

            // Start timeout
            Async.Start (timoutHandler, cancellationSource.Token)

            // Wait until we are activly listening on the rabbitMQReplyChannel
            let rec waitListening flag = async {
                if !listening <> flag then do! Async.Sleep (asyncSleepIntervalMilliseconds) 
                                           do! waitListening flag
            }

            // Wait for our listen connection to open
            waitListening true |> Async.RunSynchronously 

            // Post message!
            post exchange routingKey connection channel (Some rabbitMQReplyChannel) None (JsonConvert.SerializeObject request) 
            
            // Sometime we're expecting more than one response, so we stop listening after timeoutSecs has expired or we exceed our required responseThreshold
            let rec doneListening() = async {
                do! Async.Sleep (asyncSleepIntervalMilliseconds) 
                if not !timedOut && ( responses.contents.Count() < responseThreshold ) then do! doneListening()
            }
            
            // Wait
            doneListening() |> Async.RunSynchronously 

            // Cancel timeout
            cancellationSource.Cancel()

            // Kill the binding
            killSwitch := true
            
            // Wait for our listening connection to close
            waitListening false |> Async.RunSynchronously 

        with ex -> String.Format("{1}: Handshaking exception: {0}", ex.Message, DateTime.Now.ToLongTimeString()) |> Console.WriteLine

        // Done!
        return responses 
    }