BINARY := tyhp
DIST_DIR := dist
PROJECT := tyhp.csproj
VERSION := $(strip $(shell sed -n 's/.*<Version>\(.*\)<\/Version>/\1/p' $(PROJECT)))
ASSEMBLY_NAME := $(strip $(shell sed -n 's/.*<AssemblyName>\(.*\)<\/AssemblyName>/\1/p' $(PROJECT)))
SRC_BINARY := $(if $(ASSEMBLY_NAME),$(ASSEMBLY_NAME),$(basename $(notdir $(PROJECT))))
CURRENT_RID := $(strip $(shell dotnet --info 2>/dev/null | awk '/^ *RID:/ { print $$2; exit }'))

PLATFORMS := osx-arm64 osx-x64 linux-x64 linux-arm64 win-x64
PLATFORM_RID_osx-arm64 := osx-arm64
PLATFORM_RID_osx-x64 := osx-x64
PLATFORM_RID_linux-x64 := linux-x64
PLATFORM_RID_linux-arm64 := linux-arm64
PLATFORM_RID_win-x64 := win-x64

PUBLISH_SC_JOBS ?= 1
PUBLISH_FXD_JOBS ?= 1
PUBLISH_RETRY_ATTEMPTS ?= 3
PUBLISH_RETRY_DELAY ?= 5

ifeq ($(strip $(VERSION)),)
  $(error Could not detect project version from $(PROJECT). Ensure <Version> exists in this file.)
endif
VERSION_FORMAT_OK := $(strip $(shell printf '%s\n' "$(VERSION)" | awk '/^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$$/{print "ok"}'))
ifeq ($(VERSION_FORMAT_OK),)
  $(error Invalid VERSION '$(VERSION)' in $(PROJECT). Expected semantic version format like 805.0.0 or 805.0.0-alpha.1.)
endif

ifeq ($(strip $(CURRENT_RID)),)
  $(error Could not detect CURRENT_RID from dotnet --info. Verify dotnet is installed and working.)
endif
ifeq ($(strip $(filter $(CURRENT_RID),$(PLATFORMS))),)
  $(error Unsupported CURRENT_RID '$(CURRENT_RID)'. Supported runtimes: $(PLATFORMS).)
endif

define assert-valid-rid
$(if $(strip $(1)),,$(error RID is required. Usage: make $(2) RID=osx-arm64))
$(if $(filter $(1),$(PLATFORMS)),,$(error Unsupported RID '$(1)' for target $(2). Supported runtimes: $(PLATFORMS)))
endef

.PHONY: build build-all clean publish-sc publish-fxd publish-sc-platform-% publish-fxd-platform-%

build:
	$(call assert-valid-rid,$(CURRENT_RID),build)
	@echo "Building self-contained binary for $(CURRENT_RID) (v$(VERSION))..."
	@mkdir -p $(DIST_DIR)
	dotnet publish $(PROJECT) \
		-c Release \
		-r $(CURRENT_RID) \
		--self-contained true \
		-p:PublishSingleFile=true \
		-p:EnableCompressionInSingleFile=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-o $(DIST_DIR)/_tmp/$(CURRENT_RID)-sc
ifeq ($(findstring win,$(CURRENT_RID)),win)
	@cp $(DIST_DIR)/_tmp/$(CURRENT_RID)-sc/$(SRC_BINARY).exe $(DIST_DIR)/$(BINARY)-$(CURRENT_RID).exe
else
	@cp $(DIST_DIR)/_tmp/$(CURRENT_RID)-sc/$(SRC_BINARY) $(DIST_DIR)/$(BINARY)-$(CURRENT_RID)
endif
	@rm -rf $(DIST_DIR)/_tmp
	@echo "Built: $(DIST_DIR)/$(BINARY)-$(CURRENT_RID)$(if $(findstring win,$(CURRENT_RID)),.exe,)"

build-all:
	@echo "=== Building tyhp v$(VERSION) for all platforms ==="
	@echo ""
	@rm -rf bin obj $(DIST_DIR)/.publish-restore.stamp
	@echo "Publishing self-contained builds (-j$(PUBLISH_SC_JOBS))..."
	@$(MAKE) -j$(PUBLISH_SC_JOBS) $(PLATFORMS:%=publish-sc-platform-%)
	@echo ""
	@echo "Publishing framework-dependent builds (-j$(PUBLISH_FXD_JOBS))..."
	@$(MAKE) -j$(PUBLISH_FXD_JOBS) $(PLATFORMS:%=publish-fxd-platform-%)
	@rm -rf $(DIST_DIR)/_tmp $(DIST_DIR)/_artifacts
	@echo ""
	@echo "=== Build complete (v$(VERSION)) ==="
	@echo "Artifacts in $(DIST_DIR)/:"
	@ls -1 $(DIST_DIR)/

publish-sc-platform-%:
	$(call assert-valid-rid,$(PLATFORM_RID_$*),publish-sc-platform-%)
	@$(MAKE) publish-sc RID=$(PLATFORM_RID_$*)

publish-fxd-platform-%:
	$(call assert-valid-rid,$(PLATFORM_RID_$*),publish-fxd-platform-%)
	@$(MAKE) publish-fxd RID=$(PLATFORM_RID_$*)

# Isolated --artifacts-path per RID plus retries for transient MSB3021 / Gatekeeper EPERM.
publish-sc:
	$(call assert-valid-rid,$(RID),publish-sc)
	@echo "Publishing self-contained for $(RID)..."
	@mkdir -p $(DIST_DIR)
	@trap 'exit 130' INT TERM; \
	tmp_dir="$(DIST_DIR)/_tmp/$(RID)-sc"; \
	max_attempts=$(PUBLISH_RETRY_ATTEMPTS); attempt=1; \
	while true; do \
		rm -rf "$$tmp_dir" "$(DIST_DIR)/_artifacts/$(RID)-sc"; \
		if dotnet publish $(PROJECT) \
			--artifacts-path "$(DIST_DIR)/_artifacts/$(RID)-sc" \
			-c Release \
			-r $(RID) \
			--self-contained true \
			-p:PublishSingleFile=true \
			-p:EnableCompressionInSingleFile=true \
			-p:IncludeNativeLibrariesForSelfExtract=true \
			-o "$$tmp_dir"; \
		then \
			cp "$$tmp_dir/$(SRC_BINARY)$(if $(findstring win,$(RID)),.exe,)" \
				"$(DIST_DIR)/$(BINARY)-$(RID)$(if $(findstring win,$(RID)),.exe,)"; \
			echo "  -> $(DIST_DIR)/$(BINARY)-$(RID)$(if $(findstring win,$(RID)),.exe,)"; \
			break; \
		fi; \
		if [ $$attempt -ge $$max_attempts ]; then \
			echo "Publishing self-contained for $(RID) failed after $$max_attempts attempts"; \
			rm -rf "$$tmp_dir"; rmdir $(DIST_DIR)/_tmp 2>/dev/null || true; \
			exit 1; \
		fi; \
		echo "  Retrying self-contained $(RID) (attempt $$((attempt + 1))/$$max_attempts) in $(PUBLISH_RETRY_DELAY)s..."; \
		sleep $(PUBLISH_RETRY_DELAY); \
		attempt=$$((attempt + 1)); \
	done; \
	rm -rf "$$tmp_dir"; rmdir $(DIST_DIR)/_tmp 2>/dev/null || true

publish-fxd:
	$(call assert-valid-rid,$(RID),publish-fxd)
	@echo "Publishing framework-dependent for $(RID)..."
	@mkdir -p $(DIST_DIR)
	@trap 'exit 130' INT TERM; \
	tmp_dir="$(DIST_DIR)/_tmp/$(RID)-fxd"; \
	max_attempts=$(PUBLISH_RETRY_ATTEMPTS); attempt=1; \
	while true; do \
		rm -rf "$$tmp_dir" "$(DIST_DIR)/_artifacts/$(RID)-fxd"; \
		if dotnet publish $(PROJECT) \
			--artifacts-path "$(DIST_DIR)/_artifacts/$(RID)-fxd" \
			-c Release \
			-r $(RID) \
			--self-contained false \
			-p:PublishSingleFile=true \
			-o "$$tmp_dir"; \
		then \
			cp "$$tmp_dir/$(SRC_BINARY)$(if $(findstring win,$(RID)),.exe,)" \
				"$(DIST_DIR)/$(BINARY)-$(RID)-fxdependent$(if $(findstring win,$(RID)),.exe,)"; \
			echo "  -> $(DIST_DIR)/$(BINARY)-$(RID)-fxdependent$(if $(findstring win,$(RID)),.exe,)"; \
			break; \
		fi; \
		if [ $$attempt -ge $$max_attempts ]; then \
			echo "Publishing framework-dependent for $(RID) failed after $$max_attempts attempts"; \
			rm -rf "$$tmp_dir"; rmdir $(DIST_DIR)/_tmp 2>/dev/null || true; \
			exit 1; \
		fi; \
		echo "  Retrying framework-dependent $(RID) (attempt $$((attempt + 1))/$$max_attempts) in $(PUBLISH_RETRY_DELAY)s..."; \
		sleep $(PUBLISH_RETRY_DELAY); \
		attempt=$$((attempt + 1)); \
	done; \
	rm -rf "$$tmp_dir"; rmdir $(DIST_DIR)/_tmp 2>/dev/null || true

clean:
	@echo "Cleaning build artifacts..."
	rm -rf $(DIST_DIR)
	rm -rf bin obj
	@echo "Clean complete."
