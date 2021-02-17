﻿using com.lightstreamer.client.events;
using com.lightstreamer.client.session;
using com.lightstreamer.client.transport.providers;
using CookieManager;
using Lightstreamer.DotNet.Logging.Log;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/*
 * Copyright (c) 2004-2019 Lightstreamer s.r.l., Via Campanini, 6 - 20124 Milano, Italy.
 * All rights reserved.
 * www.lightstreamer.com
 *
 * This software is the confidential and proprietary information of
 * Lightstreamer s.r.l.
 * You shall not disclose such Confidential Information and shall use it
 * only in accordance with the terms of the license agreement you entered
 * into with Lightstreamer s.r.l.
 */
namespace com.lightstreamer.client
{
    /// <summary>
    /// Facade class for the management of the communication to
    /// Lightstreamer Server. Used to provide configuration settings, event
    /// handlers, operations for the control of the connection lifecycle,
    /// <seealso cref="Subscription"/> handling and to send messages. <br/>
    /// An instance of LightstreamerClient handles the communication with
    /// Lightstreamer Server on a specified endpoint. Hence, it hosts one "Session";
    /// or, more precisely, a sequence of Sessions, since any Session may fail
    /// and be recovered, or it can be interrupted on purpose.
    /// So, normally, a single instance of LightstreamerClient is needed. <br/>
    /// However, multiple instances of LightstreamerClient can be used,
    /// toward the same or multiple endpoints.
    /// </summary>
    public class LightstreamerClient
    {
        private bool InstanceFieldsInitialized = false;

        private void InitializeInstanceFields()
        {
            dispatcher = new EventDispatcher<ClientListener>(eventsThread);
            internalListener = new InternalListener(this);
            internalConnectionDetails = new InternalConnectionDetails(dispatcher);
            internalConnectionOptions = new InternalConnectionOptions(dispatcher, internalListener);
            manager = new SessionManager(internalConnectionOptions, internalConnectionDetails, sessionThread);
            engine = new LightstreamerEngine(internalConnectionOptions, sessionThread, eventsThread, internalListener, manager);
            messages = new MessageManager(eventsThread, sessionThread, manager, internalConnectionOptions);
            subscriptions = new SubscriptionManager(sessionThread, manager, internalConnectionOptions);
            // mpnManager = new MpnManager(manager, this, sessionThread);
            connectionOptions = new ConnectionOptions(internalConnectionOptions);
            connectionDetails = new ConnectionDetails(internalConnectionDetails);
        }


        /// <summary>
        /// A constant string representing the name of the library.
        /// </summary>
        public const string LIB_NAME = "Lightstreamer.DotNetStandard.Client";

        /// <summary>
        /// A constant string representing the version of the library.
        /// </summary>
        public static readonly string LIB_VERSION = "0.5.1".Trim();

        private static readonly Regex ext_alpha_numeric = new Regex("^[a-zA-Z0-9_]*$");

        /// <summary>
        /// Static method that permits to configure the logging system used by the library. The logging system 
        /// must respect the 
        /// <a href="log_javadoc_url_placeholder/com/lightstreamer/log/LoggerProvider.html">LoggerProvider</a> 
        /// interface. A custom class can be used to wrap any third-party 
        /// .NET logging tools. <br/>
        /// If no logging system is specified, all the generated log is discarded. <br/>
        /// The following categories are available to be consumed:
        /// <ul>
        ///  <li>lightstreamer.stream:<br/>
        ///  logs socket activity on Lightstreamer Server connections;<br/>
        ///  at INFO level, socket operations are logged;<br/>
        ///  at DEBUG level, read/write data exchange is logged.
        ///  </li>
        ///  <li>lightstreamer.protocol:<br/>
        ///  logs requests to Lightstreamer Server and Server answers;<br/>
        ///  at INFO level, requests are logged;<br/>
        ///  at DEBUG level, request details and events from the Server are logged.
        ///  </li>
        ///  <li>lightstreamer.session:<br/>
        ///  logs Server Session lifecycle events;<br/>
        ///  at INFO level, lifecycle events are logged;<br/>
        ///  at DEBUG level, lifecycle event details are logged.
        ///  </li>
        ///  <li>lightstreamer.subscriptions:<br/>
        ///  logs subscription requests received by the clients and the related updates;<br/>
        ///  at WARN level, alert events from the Server are logged;<br/>
        ///  at INFO level, subscriptions and unsubscriptions are logged;<br/>
        ///  at DEBUG level, requests batching and update details are logged.
        ///  </li>
        ///  <li>lightstreamer.actions:<br/>
        ///  logs settings / API calls.
        ///  </li>
        /// </ul>
        /// </summary>
        /// <param name="provider"> A <a href="log_javadoc_url_placeholder/com/lightstreamer/log/LoggerProvider.html">LoggerProvider</a>
        /// instance that will be used to generate log messages by the library classes. </param>
        public static void setLoggerProvider(ILoggerProvider provider)
        {
            LogManager.SetLoggerProvider(provider);
        }

        //I have to make this static, otherwise new Subscription will not be able to use 
        //it to dispatch listen-start and listen-end events 
        public static readonly EventsThread eventsThread = EventsThread.instance;

        private EventDispatcher<ClientListener> dispatcher;
        private readonly ILogger log = LogManager.GetLogger(Constants.ACTIONS_LOG);

        private InternalListener internalListener;
        private InternalConnectionDetails internalConnectionDetails;
        private InternalConnectionOptions internalConnectionOptions;
        internal readonly SessionThread sessionThread = new SessionThread();

        internal SessionManager manager;

        private LightstreamerEngine engine;

        private string lastStatus = Constants.DISCONNECTED;

        private MessageManager messages;
        private SubscriptionManager subscriptions;
        private readonly List<Subscription> subscriptionArray = new List<Subscription>();

        /// <summary>
        /// Data object that contains options and policies for the connection to 
        /// the server. This instance is set up by the LightstreamerClient object at 
        /// its own creation. <br/>
        /// Properties of this object can be overwritten by values received from a 
        /// Lightstreamer Server. 
        /// </summary>
        public ConnectionOptions connectionOptions;
        /// <summary>
        /// Data object that contains the details needed to open a connection to 
        /// a Lightstreamer Server. This instance is set up by the LightstreamerClient object at 
        /// its own creation. <br/>
        /// Properties of this object can be overwritten by values received from a 
        /// Lightstreamer Server. 
        /// </summary>
        public ConnectionDetails connectionDetails;

        /// <summary>
        /// Creates an object to be configured to connect to a Lightstreamer server
        /// and to handle all the communications with it.
        /// Each LightstreamerClient is the entry point to connect to a Lightstreamer server, 
        /// subscribe to as many items as needed and to send messages. 
        /// </summary>
        /// <param name="serverAddress"> the address of the Lightstreamer Server to
        /// which this LightstreamerClient will connect to. It is possible to specify it later
        /// by using null here. See <seealso cref="ConnectionDetails.ServerAddress"/> 
        /// for details. </param>
        /// <param name="adapterSet"> the name of the Adapter Set mounted on Lightstreamer Server 
        /// to be used to handle all requests in the Session associated with this 
        /// LightstreamerClient. It is possible not to specify it at all or to specify 
        /// it later by using null here. See <seealso cref="ConnectionDetails.AdapterSet"/> 
        /// for details.
        /// </param>
        public LightstreamerClient(string serverAddress, string adapterSet)
        {
            if (!InstanceFieldsInitialized)
            {
                InitializeInstanceFields();
                InstanceFieldsInitialized = true;
            }

            log.Info("New Lightstreamer Client instanced (library version: " + LIB_NAME + " " + LIB_VERSION + ")");

            // Environment.SetEnvironmentVariable("io.netty.allocator.type", "unpooled");
            Environment.SetEnvironmentVariable("io.netty.allocator.maxOrder", "5");

            // SessionThreadSet.sessionThreadSet.TryAdd(this.GetHashCode(), sessionThread);

            /* set circular dependencies */
            sessionThread.SessionManager = manager;
            /* */
            if (!string.ReferenceEquals(serverAddress, null))
            {
                this.connectionDetails.ServerAddress = serverAddress;
            }
            if (!string.ReferenceEquals(adapterSet, null))
            {
                this.connectionDetails.AdapterSet = adapterSet;
            }

            if (TransportFactory<WebSocketProvider>.DefaultWebSocketFactory == null)
            {
                log.Info("WebSocket not available");
                this.connectionOptions.ForcedTransport = "HTTP";
            }
            else
            {
                this.connectionOptions.ForcedTransport = null; // apply StreamSense
            }
        }

        /// <summary>
        /// Adds a listener that will receive events from the LightstreamerClient instance. 
        /// The same listener can be added to several different LightstreamerClient instances.<br/>
        /// 
        /// <b>Lifecycle:</b>  A listener can be added at any time. A call to add a listener already 
        /// present will be ignored.
        /// </summary>
        /// <param name="listener"> An object that will receive the events as documented in the 
        /// ClientListener interface.
        /// </param>
        /// <seealso cref="removeListener" />
        public virtual void addListener(ClientListener listener)
        {
            lock (this)
            {
                this.dispatcher.AddListener(listener, new ClientListenerStartEvent(this));
            }
        }

        /// <summary>
        /// Removes a listener from the LightstreamerClient instance so that it will not receive events anymore.
        /// 
        /// <b>Lifecycle:</b>  a listener can be removed at any time.
        /// </summary>
        /// <param name="listener"> The listener to be removed.
        /// </param>
        /// <seealso cref="addListener" />
        public virtual void removeListener(ClientListener listener)
        {
            lock (this)
            {
                this.dispatcher.removeListener(listener, new ClientListenerEndEvent(this));
            }
        }

        /// <summary>
        /// Returns a list containing the <seealso cref="ClientListener"/> instances that were added to this client.
        /// </summary>
        /// <returns> a list containing the listeners that were added to this client. </returns>
        /// <seealso cref="addListener" />
        public virtual IList<ClientListener> Listeners
        {
            get
            {
                lock (this)
                {
                    return this.dispatcher.Listeners;
                }
            }
        }

        /// <summary>
        /// Operation method that requests to open a Session against the configured Lightstreamer Server. <br/>
        /// When connect() is called, unless a single transport was forced through 
        /// <seealso cref="ConnectionOptions.ForcedTransport" />, the so called "Stream-Sense" mechanism is started: 
        /// if the client does not receive any answer for some seconds from the streaming connection, then it 
        /// will automatically open a polling connection. <br/>
        /// A polling connection may also be opened if the environment is not suitable for a streaming connection. <br/>
        /// Note that as "polling connection" we mean a loop of polling requests, each of which requires opening a 
        /// synchronous (i.e. not streaming) connection to Lightstreamer Server.
        /// 
        /// <b>Lifecycle:</b>  Note that the request to connect is accomplished by the client appending the request to
        /// the internal scheduler queue; this means that an invocation to <seealso cref="Status"/> right after
        /// connect() might not reflect the change yet. <br/> 
        /// When the request to connect is finally being executed, if the current status of the client is 
        /// CONNECTING, CONNECTED:* or STALLED, then nothing will be done.
        /// </summary>
        /// <seealso cref="Status" />
        /// <seealso cref="disconnect" />
        /// <seealso cref="ClientListener.onStatusChange" />
        /// <seealso cref="ConnectionDetails.ServerAddress" />
        public virtual void connect()
        {
            lock (this)
            {
                if (string.ReferenceEquals(this.connectionDetails.ServerAddress, null))
                {
                    throw new System.InvalidOperationException("Configure the server address before trying to connect");
                }

                log.Info("Connect requested");

                eventsThread.queue(() =>
                {
                    engine.connect();
                });


            }
        }

        /// <summary>
        /// Operation method that requests to close the Session opened against the configured Lightstreamer Server 
        /// (if any). <br/>
        /// When disconnect() is called, the "Stream-Sense" mechanism is stopped. <br/>
        /// Note that active Subscription instances, associated with this LightstreamerClient instance, are preserved 
        /// to be re-subscribed to on future Sessions.
        /// 
        /// @lifecycle  Note that the request to disconnect is accomplished by the client in a separate thread; this 
        /// means that an invocation to <seealso cref="Status"/> right after disconnect() might not reflect the change yet. <br/> 
        /// When the request to disconnect is finally being executed, if the status of the client is "DISCONNECTED", 
        /// then nothing will be done.
        /// </summary>
        /// <seealso cref="connect" />
        public virtual void disconnect()
        {
            lock (this)
            {
                log.Info("Disconnect requested - " + this.connectionDetails.AdapterSet);

                eventsThread.queue(() =>
                {
                    engine.disconnect();
                });
            }
        }

        /// <summary>
        /// Works just like <seealso cref="LightstreamerClient.disconnect()"/>, but also returns 
        /// a  <seealso cref="Task"/> which will be completed
        /// when all involved threads started by all <seealso cref="LightstreamerClient"/>
        /// instances have been terminated, because no more activities
        /// need to be managed and hence event dispatching is no longer necessary.
        /// Such a method is especially useful in those environments which require appropriate
        /// resource management. The method should be used in replacement
        /// of <seealso cref="LightstreamerClient.disconnect()"/> in all those circumstances 
        /// where it is indispensable to guarantee a complete shutdown of all user tasks, 
        /// in order to avoid potential memory leaks and waste resources.
        /// </summary>
        /// <returns> A Task that will be completed when all the activities launched by
        /// all  <seealso cref="LightstreamerClient"/> instances are terminated.
        /// </returns>
        /// <seealso cref="disconnect" />
        public Task DisconnectFuture() 
        {
            this.disconnect();
            Action action = () =>
            {
                // Synchronous waiting for EventsThread completion.
                eventsThread.await();

                // Synchronous waiting for SessionThread completion.
                sessionThread.await();

                log.Info("DisconnectFuture end.");
            };
            Task t1 = new Task(action);
            sessionThread.schedule(t1, 50);

            return t1;
        }
        ~LightstreamerClient() 
        {
            log.Info("Im am disposing ...");
        }

        /// <summary>
        /// Inquiry method that gets the current client status and transport (when applicable).
        /// </summary>
        /// <returns> The current client status. It can be one of the following values:
        /// <ul>
        ///  <li>"CONNECTING" the client is waiting for a Server's response in order to establish a connection;</li>
        ///  <li>"CONNECTED:STREAM-SENSING" the client has received a preliminary response from the server and 
        ///  is currently verifying if a streaming connection is possible;</li>
        ///  <li>"CONNECTED:WS-STREAMING" a streaming connection over WebSocket is active;</li>
        ///  <li>"CONNECTED:HTTP-STREAMING" a streaming connection over HTTP is active;</li>
        ///  <li>"CONNECTED:WS-POLLING" a polling connection over WebSocket is in progress;</li>
        ///  <li>"CONNECTED:HTTP-POLLING" a polling connection over HTTP is in progress;</li>
        ///  <li>"STALLED" the Server has not been sending data on an active streaming connection for longer 
        ///  than a configured time;</li>
        ///  <li>"DISCONNECTED:WILL-RETRY" no connection is currently active but one will be opened after a timeout;</li>
        ///  <li>"DISCONNECTED:TRYING-RECOVERY" no connection is currently active,
        ///  but one will be opened as soon as possible, as an attempt to recover
        ///  the current session after a connection issue;</li> 
        ///  <li>"DISCONNECTED" no connection is currently active.</li>
        /// </ul>
        /// </returns>
        /// <seealso cref="ClientListener.onStatusChange" />
        public virtual string Status
        {
            get
            {
                lock (this)
                {
                    return this.lastStatus;
                }
            }
        }

        /// <summary>
        /// Operation method that adds a Subscription to the list of "active" Subscriptions. The Subscription cannot already 
        /// be in the "active" state. <br/>
        /// Active subscriptions are subscribed to through the server as soon as possible (i.e. as soon as there is a 
        /// session available). Active Subscription are automatically persisted across different sessions as long as a 
        /// related unsubscribe call is not issued.
        /// 
        /// <b>Lifecycle:</b>  Subscriptions can be given to the LightstreamerClient at any time. Once done the Subscription 
        /// immediately enters the "active" state. <br/>
        /// Once "active", a Subscription instance cannot be provided again to a LightstreamerClient unless it is 
        /// first removed from the "active" state through a call to <seealso cref="unsubscribe"/>. <br/>
        /// Also note that forwarding of the subscription to the server is made appending the request to the internal scheduler. <br/>
        /// A successful subscription to the server will be notified through a <seealso cref="SubscriptionListener.onSubscription"/>
        /// event.
        /// </summary>
        /// <param name="subscription"> A Subscription object, carrying all the information needed to process real-time values.
        /// </param>
        /// <seealso cref="unsubscribe" />
        public virtual void subscribe(Subscription subscription)
        {
            lock (this)
            {
                subscription.setActive();
                subscriptionArray.Add(subscription);
                eventsThread.queue(() =>
                {
                    subscriptions.add(subscription);
                });

            }
        }

        /// <summary>
        /// Operation method that removes a Subscription that is currently in the "active" state. <br/> 
        /// By bringing back a Subscription to the "inactive" state, the unsubscription from all its items is 
        /// requested to Lightstreamer Server.
        /// 
        /// <b>Lifecycle:</b>  Subscription can be unsubscribed from at any time. Once done the Subscription immediately 
        /// exits the "active" state. <br/>
        /// Note that forwarding of the unsubscription to the server is made appending the request to the internal scheduler. <br/>
        /// The unsubscription will be notified through a <seealso cref="SubscriptionListener.onUnsubscription" /> event.
        /// </summary>
        /// <param name="subscription"> An "active" Subscription object that was activated by this LightstreamerClient 
        /// instance. </param>
        public virtual void unsubscribe(Subscription subscription)
        {
            lock (this)
            {
                subscription.setInactive();
                subscriptionArray.Remove(subscription);
                eventsThread.queue(() =>
                {
                    subscriptions.remove(subscription);
                });
            }
        }

        /// <summary>
        /// Inquiry method that returns a list containing all the Subscription instances that are 
        /// currently "active" on this LightstreamerClient. <br/>
        /// Internal second-level Subscription are not included.
        /// </summary>
        /// <returns> A list, containing all the Subscription currently "active" on this LightstreamerClient. <br/>
        /// The list can be empty. </returns>
        /// <seealso cref="subscribe" />
        public virtual IList<Subscription> Subscriptions
        {
            get
            {
                lock (this)
                {
                    //2nd level subscriptions are not in the subscriptionArray
                    return new List<Subscription>(subscriptionArray);
                }
            }
        }

        /// <summary>
        /// A simplified version of the <seealso cref="sendMessage(string, string, int, ClientMessageListener, bool)" />.
        /// The internal implementation will call
        /// <code>
        ///   sendMessage(message,null,-1,null,false);
        /// </code>
        /// Note that this invocation involves no sequence and no listener, hence an optimized
        /// fire-and-forget behavior will be applied.
        /// </summary>
        /// <param name="message"> a text message, whose interpretation is entirely demanded to the Metadata Adapter
        /// associated to the current connection. </param>
        public virtual void sendMessage(string message)
        {
            lock (this)
            {
                this.sendMessage(message, null, -1, null, false);
            }
        }

        /// <summary>
        /// Operation method that sends a message to the Server. The message is interpreted and handled by 
        /// the Metadata Adapter associated to the current Session. This operation supports in-order 
        /// guaranteed message delivery with automatic batching. In other words, messages are guaranteed 
        /// to arrive exactly once and respecting the original order, whatever is the underlying transport 
        /// (HTTP or WebSockets). Furthermore, high frequency messages are automatically batched, if necessary,
        /// to reduce network round trips. <br/>
        /// Upon subsequent calls to the method, the sequential management of the involved messages is guaranteed. 
        /// The ordering is determined by the order in which the calls to sendMessage are issued.
        /// However, any message that, for any reason, doesn't reach the Server can be discarded by the Server 
        /// if this causes the subsequent message to be kept waiting for longer than a configurable timeout. 
        /// Note that, because of the asynchronous transport of the requests, if a zero or very low timeout is 
        /// set for a message, it is not guaranteed that the previous message can be processed, even if no communication 
        /// issues occur. <br/>
        /// Sequence identifiers can also be associated with the messages. In this case, the sequential management is 
        /// restricted to all subsets of messages with the same sequence identifier associated. <br/>
        /// Notifications of the operation outcome can be received by supplying a suitable listener. The supplied 
        /// listener is guaranteed to be eventually invoked; listeners associated with a sequence are guaranteed 
        /// to be invoked sequentially. <br/>
        /// The "UNORDERED_MESSAGES" sequence name has a special meaning. For such a sequence, immediate processing 
        /// is guaranteed, while strict ordering and even sequentialization of the processing is not enforced. 
        /// Likewise, strict ordering of the notifications is not enforced. However, messages that, for any reason, 
        /// should fail to reach the Server whereas subsequent messages had succeeded, might still be discarded after 
        /// a server-side timeout. <br/>
        /// Moreover, if "UNORDERED_MESSAGES" is used and no listener is supplied, a "fire and forget" scenario
        /// is assumed. In this case, no checks on missing, duplicated or overtaken messages are performed at all,
        /// so as to optimize the processing and allow the highest possible throughput.
        /// 
        /// <b>Lifecycle:</b>  Since a message is handled by the Metadata Adapter associated to the current connection, a
        /// message can be sent only if a connection is currently active. If the special enqueueWhileDisconnected 
        /// flag is specified it is possible to call the method at any time and the client will take care of sending
        /// the message as soon as a connection is available, otherwise, if the current status is "DISCONNECTED*", 
        /// the message will be abandoned and the <seealso cref="ClientMessageListener.onAbort"/> event will be fired. <br/>
        /// Note that, in any case, as soon as the status switches again to "DISCONNECTED*", any message still pending 
        /// is aborted, including messages that were queued with the enqueueWhileDisconnected flag set to true. <br/>
        /// Also note that forwarding of the message to the server is made appending the request to
        /// the internal scheduler queue; hence, if a message 
        /// is sent while the connection is active, it could be aborted because of a subsequent disconnection. 
        /// In the same way a message sent while the connection is not active might be sent because of a subsequent
        /// connection.
        /// </summary>
        /// <param name="message"> a text message, whose interpretation is entirely demanded to the Metadata Adapter
        /// associated to the current connection. </param>
        /// <param name="sequence"> an alphanumeric identifier, used to identify a subset of messages to be managed in sequence; 
        /// underscore characters are also allowed. If the "UNORDERED_MESSAGES" identifier is supplied, the message will 
        /// be processed in the special way described above. The parameter is optional; if set to null, "UNORDERED_MESSAGES" 
        /// is used as the sequence name. </param>
        /// <param name="delayTimeout"> a timeout, expressed in milliseconds. If higher than the Server default timeout, the 
        /// latter will be used instead. <br/> 
        /// The parameter is optional; if a negative value is supplied, the Server default timeout will be applied. <br/>
        /// This timeout is ignored for the special "UNORDERED_MESSAGES" sequence, for which a custom server-side 
        /// timeout applies. </param>
        /// <param name="listener"> an object suitable for receiving notifications about the processing outcome. The parameter is 
        /// optional; if not supplied, no notification will be available. </param>
        /// <param name="enqueueWhileDisconnected"> if this flag is set to true, and the client is in a disconnected status when
        /// the provided message is handled, then the message is not aborted right away but is queued waiting for a new
        /// session. Note that the message can still be aborted later when a new session is established. </param>
        public virtual void sendMessage(string message, string sequence, int delayTimeout, ClientMessageListener listener, bool enqueueWhileDisconnected)
        {
            lock (this)
            {

                if (string.ReferenceEquals(sequence, null))
                {
                    sequence = Constants.UNORDERED_MESSAGES;
                }
                else if (!ext_alpha_numeric.Match(sequence).Success)
                {
                    throw new System.ArgumentException("The given sequence name is not valid, use only alphanumeric characters plus underscore, or null");
                }

                string seq = sequence;
                eventsThread.queue(() =>
                {
                    messages.send(message, seq, delayTimeout, listener, enqueueWhileDisconnected);
                });
            }
        }

        /// <summary>
        /// Static method that can be used to share cookies between connections to the Server
        /// (performed by this library) and connections to other sites that are performed
        /// by the application. With this method, cookies received by the application
        /// can be added (or replaced if already present) to the cookie set used by the
        /// library to access the Server. Obviously, only cookies whose domain is compatible
        /// with the Server domain will be used internally.
        /// <br/>More precisely, this explicit sharing is only needed when the library uses
        /// its own cookie storage. This depends on the availability of a default global storage.
        /// <ul><li>
        /// In fact, the library will setup its own local cookie storage only if, upon the first
        /// usage of the cookies, a default CookieHandler is not available;
        /// then it will always stick to the internal storage.
        /// </li><li>
        /// On the other hand, if a default CookieHandler is available
        /// upon the first usage of the cookies, the library, from then on, will always stick
        /// to the default it finds upon each request; in this case, the cookie storage will be
        /// already shared with the rest of the application. However, whenever a default
        /// CookieHandler of type different from CookieManager
        /// is found, the library will not be able to use it and will skip cookie handling.
        /// </li></ul>
        /// 
        /// <b>Lifecycle:</b>  This method should be invoked before calling the
        /// <seealso cref="LightstreamerClient.connect"/> method. However it can be invoked at any time;
        /// it will affect the internal cookie set immediately and the sending of cookies
        /// on the next HTTP request or WebSocket establishment.
        /// </summary>
        /// <param name="uri"> the URI from which the supplied cookies were received. It cannot be null.
        /// </param>
        /// <param name="cookies"> a list of cookies, represented in the HttpCookie type.
        /// </param>
        /// <seealso cref="getCookies" />
        public static void addCookies(Uri uri, IList<HttpCookie> cookies)
        {
            CookieHelper.addCookies(uri, cookies);
        }

        /// <summary>
        /// Static inquiry method that can be used to share cookies between connections to the Server
        /// (performed by this library) and connections to other sites that are performed
        /// by the application. With this method, cookies received from the Server can be
        /// extracted for sending through other connections, according with the URI to be accessed.
        /// <br/>See <seealso cref="addCookies" /> for clarifications on when cookies are directly stored
        /// by the library and when not.
        /// </summary>
        /// <param name="uri"> the URI to which the cookies should be sent, or null.
        /// </param>
        /// <returns> an immutable list with the various cookies that can
        /// be sent in a HTTP request for the specified URI. If a null URI was supplied,
        /// all available non-expired cookies will be returned.</returns>
        public static IList<HttpCookie> getCookies(Uri uri)
        {
            return CookieHelper.getCookies(uri);
        }

        /// <summary>
        /// Provides a mean to control the way TLS certificates are evaluated, with the possibility to accept untrusted ones.
        /// 
        /// <b>Lifecycle:</b>  May be called only once before creating any LightstreamerClient instance.
        /// </summary>
        public static RemoteCertificateValidationCallback TrustManagerFactory
        {
            set
            {
                ServicePointManager.ServerCertificateValidationCallback += value;
            }
        }

        internal virtual bool setStatus(string status)
        {
            lock (this)
            {
                if (!this.lastStatus.Equals(status))
                {
                    this.lastStatus = status;
                    return true;
                }
                return false;

            }
        }

        private class InternalListener : ClientListener
        {
            private readonly LightstreamerClient outerInstance;

            public InternalListener(LightstreamerClient outerInstance)
            {
                this.outerInstance = outerInstance;
            }


            public virtual void onListenEnd(LightstreamerClient client)
            {
                //useless
            }

            public virtual void onListenStart(LightstreamerClient client)
            {
                //useless
            }

            public virtual void onServerError(int errorCode, string errorMessage)
            {
                outerInstance.dispatcher.dispatchEvent(new ClientListenerServerErrorEvent(errorCode, errorMessage));
            }

            public virtual void onStatusChange(string status)
            {
                if (outerInstance.setStatus(status))
                {
                    outerInstance.dispatcher.dispatchEvent(new ClientListenerStatusChangeEvent(status));
                }
            }

            public virtual void onPropertyChange(string property)
            {
                switch (property)
                {
                    case "requestedMaxBandwidth":
                        eventsThread.queue(() =>
                        {
                            outerInstance.engine.onRequestedMaxBandwidthChanged();
                        });
                        break;

                    case "reverseHeartbeatInterval":
                        eventsThread.queue(() =>
                        {
                            outerInstance.engine.onReverseHeartbeatIntervalChanged();
                        });
                        break;

                    case "forcedTransport":
                        eventsThread.queue(() =>
                        {
                            outerInstance.engine.onForcedTransportChanged();
                        });
                        break;

                    default:
                        //won't happen
                        outerInstance.log.Error("Unexpected call to internal onPropertyChange");

                        break;
                }
            }
        }
    }
}