﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace MonoWorker.Core
{
    public class SimpleInstanceService
    {
        public static readonly SimpleInstanceService Instance = new SimpleInstanceService();
        public readonly Dictionary<long, InstanceWrapper> instances = new Dictionary<long, InstanceWrapper>();
        public static readonly string MessagePrefix = $"{typeof(SimpleInstanceService).FullName}::";
        public static readonly string InitMessagePrefix = $"{nameof(InitInstance)}::";
        public static readonly string InitResultMessagePrefix = $"{nameof(InitInstanceResult)}::";
        public static readonly string DiposeMessagePrefix = $"{nameof(InitInstance)}::";

        public static void Init()
        {
            Instance.InnerInit();
        }

        private void InnerInit()
        {
            MessageService.Message += OnMessage;
        }

        private void OnMessage(object sender, string rawMessage)
        {
            if (rawMessage.StartsWith(MessagePrefix) == false)
            {
                return;
            }

            rawMessage = rawMessage.Substring(MessagePrefix.Length);

            if (rawMessage.StartsWith(InitMessagePrefix)) {
                rawMessage = rawMessage.Substring(InitMessagePrefix.Length);
                InitInstance(rawMessage);
                return;
            }
        }

        public void InitInstance(string initMessage)
        {
            var splitMessage = initMessage.Split(';');
            var id = long.Parse(splitMessage[0]);
            var typeName = splitMessage[1];
            var assemblyName = splitMessage[2];

            InitInstanceResult result = InitInstance(id, typeName, assemblyName);

            MessageService.PostMessage(
                $"{MessagePrefix}{InitResultMessagePrefix}" +
                $"{(result.IsSuccess ? "1" : "0")}:" +
                $"{result.ExceptionMessage}:" +
                $"{result.FullExceptionString}");
        }

        public InitInstanceResult InitInstance(long id, string typeName, string assemblyName)
        {
            var InstanceWrapper = new InstanceWrapper();
            var result = InitInstance(typeName, assemblyName,
                () => (IWorkerMessageService)(InstanceWrapper.Services = new InjectableMessageService()));
            InstanceWrapper.Instance = result.Instance;
            if (result.IsSuccess)
            {
                instances[id] = InstanceWrapper;
            }
            else
            {
                InstanceWrapper.Dispose();
            }

            return result;
        }

        private bool DisposeInstance(long id)
        {
            if (!instances.TryGetValue(id, out var instanceWrapper)) {
                return false;
            }

            try
            {
                instanceWrapper.Dispose();

                instances.Remove(id);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static InitInstanceResult InitInstance(string typeName, string assemblyName, Func<IWorkerMessageService> workerMessageServiceFactory)
        {
            try
            {
                var type = Type.GetType($"{typeName}, {assemblyName}", true);
                var constructors = type.GetConstructors();
                ConstructorInfo constructorInfo;
                var lastMatchArgCount = -1;
                foreach (var constructor in constructors)
                {
                    var parameters = constructor.GetParameters();
                    if (parameters.Length == 0 && lastMatchArgCount < 0)
                    {
                        lastMatchArgCount = 0;
                        constructorInfo = constructor;
                        continue;
                    }

                    if (parameters.Length == 1 && lastMatchArgCount < 1)
                    {
                        if (parameters[0].ParameterType == typeof(IWorkerMessageService))
                        {
                            lastMatchArgCount = 1;
                            constructorInfo = constructor;
                            continue;
                        }
                    }
                }

                object instance;

                if (lastMatchArgCount == 0)
                {
                    instance = Activator.CreateInstance(type);
                }
                else if (lastMatchArgCount == 1)
                {
                    instance = Activator.CreateInstance(type, workerMessageServiceFactory());
                }
                else {
                    throw new InvalidOperationException($"Unable to find compatible constructor for activating type '{type}'.");
                }

                return new InitInstanceResult()
                {
                    Instance = instance,
                    IsSuccess = true
                };
            }
            catch (Exception e)
            {
                return new InitInstanceResult
                {
                    ExceptionMessage = e.Message,
                    FullExceptionString = e.ToString(),
                    Exception = e,
                    IsSuccess = false
                };
            }
        }
    }
}
