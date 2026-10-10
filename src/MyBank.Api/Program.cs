using MyBank.Api.Endpoints;
using MyBank.Api.Extensions;
using MyBank.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMyBankCore(builder.Configuration);
builder.AddMyBankAuth();
builder.AddMyBankHttp();

var app = builder.Build();

app.UseMyBankPipeline();

app.MapDevTokenEndpoint();
app.MapAccountEndpoints();
app.MapHealthEndpoints();

app.Run();
