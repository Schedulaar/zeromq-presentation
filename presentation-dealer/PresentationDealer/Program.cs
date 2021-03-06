﻿using System;
using NetMQ;
using NetMQ.Sockets;
using NetMQ.WebSockets;

/*
 Unfortunately NetMq.WebSockets WSPublisher has a problem
 forcing the process to quit, once a node looses connection while sending.
 See https://github.com/NetMQ/NetMQ.WebSockets/issues/9 for more information.
 */

namespace PresentationDealer {
    static class Program {
        private static void Main(string[] args) {
            var deciSecond = new TimeSpan(10000);
            
            using (var subscriber = new SubscriberSocket()) // For receiving updates from presentation host
            using (var publisher = new WSPublisher()) // For publishing updates from presentation host to audience
            using (var responder = new WSRouter()) // Handling on-demand requests for late-joining or crashing clients
            {
                subscriber.Bind("tcp://*:3000");
                subscriber.SubscribeToAnyTopic();
                publisher.Bind("ws://*:3001");
                responder.Bind("ws://*:3002");

                byte step = 0;
                subscriber.ReceiveReady += (_, __) => {
                    if (!subscriber.TryReceiveFrameBytes(deciSecond, out var received)) return;
                    step = received[0];
                    Console.Out.WriteLine("Sending " + step + " to audience.");
                    publisher.TrySendFrame(deciSecond, new[] {step});
                };
                responder.ReceiveReady += (_, __) => {
                    NetMQMessage msg = null;
                    if (!responder.TryReceiveMultipartMessage(deciSecond, ref msg)) return;
                    var identity = msg.Pop().Buffer;
                    var request = msg.Pop().ConvertToString();
                    msg.Clear();
                    if (request == "Which slide are we on?")
                        responder.TrySendMultipartBytes(deciSecond, identity, new[] {step});
                    else {
                        if (!responder.TrySendFrame(deciSecond, identity, true)) return;
                        responder.TrySendFrameEmpty(deciSecond);
                    }
                };
                new NetMQPoller {subscriber, responder}.Run(); // Polling both subscriber and router sockets.
            }
        }
    }
}