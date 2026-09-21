// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.
using ClaudeNim.Aot.Configuration;
using ClaudeNim.Aot.Hosting;
using ClaudeNim.Aot.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateSlimBuilder(args);

// Environment variables win over the settings file: the proxy is usually run from a shell or a
// container, where a credential in a file on disk is the thing you least want.
_ = builder.Configuration.AddClaudeNimEnvironment();

// The slim builder's JSON options still default to reflection. Pointing them at the generated
// context is what lets minimal API's own serialization run under native AOT.
_ = builder.Services.ConfigureHttpJsonOptions(static options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ProxyJsonContext.Default));

_ = builder.Services.AddClaudeNimProxy(builder.Configuration);

var app = builder.Build();

_ = app.MapClaudeNimEndpoints();

await app.RunAsync().ConfigureAwait(false);
