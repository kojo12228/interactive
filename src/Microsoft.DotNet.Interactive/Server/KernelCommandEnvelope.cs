﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.DotNet.Interactive.Commands;

namespace Microsoft.DotNet.Interactive.Server
{
    public abstract class KernelCommandEnvelope : IKernelCommandEnvelope
    {
        private static readonly ConcurrentDictionary<Type, Func<KernelCommand, IKernelCommandEnvelope>> _envelopeFactories =
            new();

        private static ConcurrentDictionary<string, Type> _envelopeTypesByCommandTypeName;

        private static ConcurrentDictionary<string, Type> _commandTypesByCommandTypeName;


        static KernelCommandEnvelope()
        {
            ResetToDefaults();
        }

        internal static Type CommandTypeByName(string name) => _commandTypesByCommandTypeName[name];

        private readonly KernelCommand _command;

        protected KernelCommandEnvelope(KernelCommand command)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
        }

        public abstract string CommandType { get; }

        public string Token => _command.GetToken();

        KernelCommand IKernelCommandEnvelope.Command => _command;

        internal static void RegisterCommandTypeForSerialization<T>() where T : KernelCommand
        {
            var commandEnvelopeType = typeof(KernelCommandEnvelope<T>);
            var commandType = typeof(T);
            var commandTypeName = commandType.Name;

            _envelopeTypesByCommandTypeName[commandTypeName] = commandEnvelopeType;
            _commandTypesByCommandTypeName[commandTypeName] = commandType;
        }

        public static void ResetToDefaults()
        {
            _envelopeTypesByCommandTypeName = new ConcurrentDictionary<string, Type>
            {
                [nameof(AddPackage)] = typeof(KernelCommandEnvelope<AddPackage>),
                [nameof(ChangeWorkingDirectory)] = typeof(KernelCommandEnvelope<ChangeWorkingDirectory>),
                [nameof(DisplayError)] = typeof(KernelCommandEnvelope<DisplayError>),
                [nameof(DisplayValue)] = typeof(KernelCommandEnvelope<DisplayValue>),
                [nameof(ParseInteractiveDocument)] = typeof(KernelCommandEnvelope<ParseInteractiveDocument>),
                [nameof(RequestCompletions)] = typeof(KernelCommandEnvelope<RequestCompletions>),
                [nameof(RequestDiagnostics)] = typeof(KernelCommandEnvelope<RequestDiagnostics>),
                [nameof(RequestHoverText)] = typeof(KernelCommandEnvelope<RequestHoverText>),
                [nameof(RequestSignatureHelp)] = typeof(KernelCommandEnvelope<RequestSignatureHelp>),
                [nameof(SendEditableCode)] = typeof(KernelCommandEnvelope<SendEditableCode>),
                [nameof(SerializeInteractiveDocument)] = typeof(KernelCommandEnvelope<SerializeInteractiveDocument>),
                [nameof(SubmitCode)] = typeof(KernelCommandEnvelope<SubmitCode>),
                [nameof(UpdateDisplayedValue)] = typeof(KernelCommandEnvelope<UpdateDisplayedValue>),
                [nameof(Quit)] = typeof(KernelCommandEnvelope<Quit>),
                [nameof(Cancel)] = typeof(KernelCommandEnvelope<Cancel>),
                [nameof(RequestValue)] = typeof(KernelCommandEnvelope<RequestValue>),
                [nameof(RequestValueNames)] = typeof(KernelCommandEnvelope<RequestValueNames>)
            };

            _commandTypesByCommandTypeName = new ConcurrentDictionary<string, Type>(_envelopeTypesByCommandTypeName
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.GetGenericArguments()[0]));
        }

        public static IKernelCommandEnvelope Create(KernelCommand command)
        {
            var factory = _envelopeFactories.GetOrAdd(
                command.GetType(),
                commandType =>
                {
                    var type = command.GetType();

                    var genericType = _envelopeTypesByCommandTypeName[type.Name];

                    var constructor = genericType.GetConstructors().Single();

                    var commandParameter = Expression.Parameter(
                        typeof(KernelCommand),
                        "c");

                    var newExpression = Expression.New(
                        constructor,
                        Expression.Convert(commandParameter, commandType));

                    var expression = Expression.Lambda<Func<KernelCommand, IKernelCommandEnvelope>>(
                        newExpression,
                        commandParameter);

                    return expression.Compile();
                });

            var envelope = factory(command);

            return envelope;
        }

        public static IKernelCommandEnvelope Deserialize(string json)
        {
            var jsonObject = JsonDocument.Parse(json);

            return Deserialize(jsonObject.RootElement);
        }

        public static IKernelCommandEnvelope Deserialize(JsonElement json)
        {
            var commandTypeJson = string.Empty;
            string commandJson;
            var token = string.Empty;
            
            if (json.TryGetProperty("commandType", out var commandTypeProperty))
            {
                commandTypeJson = commandTypeProperty.GetString();
            }

            if (string.IsNullOrWhiteSpace(commandTypeJson))
            {
                return null;
            }

            var commandType = CommandTypeByName(commandTypeJson);
            if (json.TryGetProperty("command", out var commandJsonProperty))
            {
                commandJson = commandJsonProperty.GetRawText();
            }
            else
            {
                return null;
            }

       
            var command = (KernelCommand) JsonSerializer.Deserialize( commandJson,commandType, Serializer.JsonSerializerOptions);

           
            if (json.TryGetProperty("token", out var tokenProperty))
            {
                token = tokenProperty.GetString();
            }
            if (token is not null)
            {
                command.SetToken(token);
            }

            return Create(command);
        }

        public static string Serialize(KernelCommand command) => Serialize(Create(command));

        public static string Serialize(IKernelCommandEnvelope envelope)
        {
            var serializationModel = new SerializationModel
            {
                command = envelope.Command,
                commandType = envelope.CommandType,
                token = envelope.Token
            };

            return JsonSerializer.Serialize(
                serializationModel,
                Serializer.JsonSerializerOptions);
        }

        internal class SerializationModel
        {
            public string token { get; set; }

            public string commandType { get; set; }

            public object command { get; set; }
        }
    }
}