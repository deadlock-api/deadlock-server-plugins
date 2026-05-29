# Deadworks plugin dev loop with hot-reload.
#
#   make dev MODE=deathmatch        # build mode's plugins, start server, watch + hot-reload
#   make dev MODE=trooper-invasion PORT=27018
#
# `make dev` builds every plugin in the chosen game mode (from gamemodes.json)
# into ./.dev-plugins, starts the Deadworks server (built from ../deadworks) with
# that folder bind-mounted, then watches each plugin's source. Editing and saving
# any .cs file rebuilds that plugin and the running server hot-reloads it — no
# restart. Ctrl-C stops the watchers and the server.
#
# Requirements: Docker + Docker Compose, the .NET 10 SDK, jq, and a sibling
# ../deadworks checkout. Copy .env.example to .env and fill in STEAM_LOGIN /
# STEAM_PASSWORD before the first run.

SHELL := /bin/bash
.SHELLFLAGS := -eu -o pipefail -c
.ONESHELL:

MODE        ?=
PORT        ?= 27015
LIVE_DIR    := .dev-plugins
COMPOSE     := docker-compose.dev.yml
DEADWORKS   := ../deadworks
GAMEMODES   := gamemodes.json
NOOP        := .staging/deploy-noop
DC          := MODE='$(MODE)' PORT='$(PORT)' docker compose -f $(COMPOSE)
PUBLISH_ARGS := --nologo -p:NoWarn=CS1591 -p:DeadlockManagedDir='$(abspath $(NOOP))'

.DEFAULT_GOAL := help

.PHONY: help list dev build watch up down stop logs clean check-env

help: ## Show this help
	@echo "Deadworks plugin dev loop"
	@echo
	@echo "Usage: make <target> MODE=<game-mode> [PORT=<port>]"
	@echo
	@echo "Targets:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
	  | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}'
	@echo
	@echo "Game modes (from $(GAMEMODES)):"
	@jq -r 'keys[] | "  " + .' $(GAMEMODES)

list: ## List available game modes and their plugins
	@jq -r 'to_entries[] | "\(.key):\n  " + (.value | join("\n  "))' $(GAMEMODES)

# Resolve + validate the plugin list for MODE; fail with a helpful message.
define require_mode
	if [ -z '$(MODE)' ]; then
		echo "error: set MODE=<game-mode>. Available:" >&2
		jq -r 'keys[] | "  " + .' $(GAMEMODES) >&2
		exit 2
	fi
	plugins=$$(jq -r '.["$(MODE)"][]?' $(GAMEMODES))
	if [ -z "$$plugins" ]; then
		echo "error: unknown mode '$(MODE)'. Available:" >&2
		jq -r 'keys[] | "  " + .' $(GAMEMODES) >&2
		exit 2
	fi
endef

# Map a plugin folder name to its .csproj (skips folders without one).
define csproj_for
$$(ls "$1"/*.csproj 2>/dev/null | head -1)
endef

check-env:
	@if [ ! -f .env ]; then
		echo "error: .env not found — copy .env.example to .env and set STEAM_LOGIN / STEAM_PASSWORD" >&2
		exit 2
	fi
	@if [ ! -d '$(DEADWORKS)' ]; then
		echo "error: sibling deadworks checkout not found at '$(DEADWORKS)'" >&2
		exit 2
	fi

build: ## Build a mode's plugins once into ./.dev-plugins (no watch)
	@$(require_mode)
	rm -rf '$(LIVE_DIR)'; mkdir -p '$(LIVE_DIR)'   # only this mode's plugins
	for p in $$plugins; do
		csproj=$(call csproj_for,$$p)
		if [ -z "$$csproj" ]; then echo "  (skip $$p: no .csproj)"; continue; fi
		echo "==> building $$p"
		dotnet publish "$$csproj" -o '$(LIVE_DIR)' $(PUBLISH_ARGS)
	done

dev: check-env ## Start the server and hot-reload all of MODE's plugins (Ctrl-C to stop)
	@$(require_mode)
	rm -rf '$(LIVE_DIR)'; mkdir -p '$(LIVE_DIR)'   # only this mode's plugins load

	# 1. Warm-up build (serial) so the shared DeadworksManaged.Api builds once —
	#    avoids parallel build races between the per-plugin watchers below.
	for p in $$plugins; do
		csproj=$(call csproj_for,$$p)
		if [ -z "$$csproj" ]; then echo "  (skip $$p: no .csproj)"; continue; fi
		echo "==> building $$p"
		dotnet publish "$$csproj" -o '$(LIVE_DIR)' $(PUBLISH_ARGS)
	done

	# 2. Start (or rebuild) the dev server, detached.
	echo "==> starting server for mode '$(MODE)' on port $(PORT)"
	$(DC) up -d --build

	# 3. Tear everything down cleanly on Ctrl-C / exit.
	cleanup() { echo; echo "==> stopping"; kill $$(jobs -p) 2>/dev/null || true; $(DC) stop; }
	trap cleanup EXIT INT TERM

	# 4. One file-watcher per plugin, each re-publishing into the live folder.
	echo "==> watching $$(echo $$plugins | wc -w) plugin(s) — edit + save to hot-reload"
	for p in $$plugins; do
		csproj=$(call csproj_for,$$p)
		[ -z "$$csproj" ] && continue
		dotnet watch --project "$$csproj" publish -o '$(LIVE_DIR)' $(PUBLISH_ARGS) \
			--non-interactive &
	done

	# 5. Follow server logs in the foreground; wait blocks until Ctrl-C.
	$(DC) logs -f &
	wait

watch: ## Watch + hot-reload MODE's plugins (assumes the server is already up)
	@$(require_mode)
	mkdir -p '$(LIVE_DIR)'
	trap 'kill $$(jobs -p) 2>/dev/null || true' EXIT INT TERM
	for p in $$plugins; do
		csproj=$(call csproj_for,$$p)
		[ -z "$$csproj" ] && continue
		echo "==> watching $$p"
		dotnet watch --project "$$csproj" publish -o '$(LIVE_DIR)' $(PUBLISH_ARGS) &
	done
	wait

up: check-env ## Start the dev server detached (no plugin building)
	@$(require_mode)
	$(DC) up -d --build

down: ## Stop and remove the dev server container
	@$(DC) down

stop: ## Stop the dev server container (keep it for a fast restart)
	@$(DC) stop

logs: ## Follow the dev server logs
	@$(DC) logs -f

clean: ## Remove the live plugins folder
	@rm -rf '$(LIVE_DIR)' '$(NOOP)'
	@echo "removed $(LIVE_DIR)"
