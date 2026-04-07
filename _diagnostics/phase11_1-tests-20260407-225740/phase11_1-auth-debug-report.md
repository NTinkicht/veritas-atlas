# Phase 11.1 Auth Debug Verification Report

Generated: 2026-04-07 22:57:40
BaseUrl: https://localhost:7091

## Public diagnostics endpoint works
- Result: PASS
- Detail: Issuer=VeritasAtlas, Audience=VeritasAtlasUsers

## Invalid login rejected
- Result: PASS
- Detail: Le serveur distant a retourné une erreur : (401) Non autorisé.

## Valid login accepted
- Result: PASS
- Detail: OK

## Auth me works
- Result: PASS
- Detail:  / admin

## Protected diagnostics works
- Result: PASS
- Detail: Claims=7

## Protected workflow accepts token
- Result: FAIL
- Detail: Le serveur distant a retourné une erreur : (500) Erreur interne du serveur.

## Protected audit accepts token
- Result: PASS
- Detail: OK

