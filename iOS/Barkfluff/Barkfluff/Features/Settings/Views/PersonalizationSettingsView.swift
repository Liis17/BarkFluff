//
//  PersonalizationSettingsView.swift
//  Barkfluff (iOS)
//
//  Раздел «Персонализация» в настройках. Четыре блока:
//  1. Превью профиля (постер + аватар + имя + кнопка смены постера).
//  2. Внешний вид сообщений (превью пузырей + слайдер закругления).
//  3. Фон чата (тогл блюра, слайдеры размытия и затемнения).
//  4. Изображения фона (сетка с добавлением и удалением).
//

import SwiftUI
import BFCore

struct PersonalizationSettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: PersonalizationSettingsViewModel?

    @AppStorage("folders.compact") private var compactFolders: Bool = false
    @AppStorage("folders.excludeFromAll") private var excludeFolderChatsFromAll: Bool = false

    var body: some View {
        Group {
            if let viewModel {
                content(viewModel: viewModel)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .navigationTitle("Персонализация")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            if viewModel == nil {
                let vm = PersonalizationSettingsViewModel(
                    userService: container.userService,
                    fileService: container.fileService,
                    container: container
                )
                viewModel = vm
                await vm.load()
            }
        }
    }

    @ViewBuilder
    private func content(viewModel vm: PersonalizationSettingsViewModel) -> some View {
        @Bindable var vm = vm
        @Bindable var settings = container.personalizationSettings

        Form {
            // Ошибки
            if let error = vm.errorMessage {
                Section {
                    HStack(alignment: .top, spacing: Theme.Spacing.sm) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundStyle(.red)
                        Text(error)
                            .font(.callout)
                            .foregroundStyle(.red)
                        Spacer()
                        Button("Скрыть") { vm.errorMessage = nil }
                            .buttonStyle(.borderless)
                            .font(.callout)
                    }
                }
            }

            // 1. Постер профиля
            Section {
                PosterPreviewCard(viewModel: vm)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
            }

            // 2. Внешний вид сообщений
            Section("Внешний вид сообщений") {
                BubblePreviewView(cornerRadius: settings.bubbleCornerRadius)
                    .listRowInsets(EdgeInsets(top: 8, leading: 0, bottom: 8, trailing: 0))
                    .listRowBackground(Color.clear)

                HStack {
                    Text("Закругление")
                    Slider(
                        value: Binding(
                            get: { Double(settings.bubbleCornerRadius) },
                            set: { settings.bubbleCornerRadius = Int($0.rounded()) }
                        ),
                        in: 0...30,
                        step: 1
                    )
                    Text("\(settings.bubbleCornerRadius) pt")
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(width: 56, alignment: .trailing)
                }
            }

            // 3. Фон чата
            Section("Фон чата") {
                Toggle("Размытие", isOn: $settings.backgroundBlurEnabled)

                if settings.backgroundBlurEnabled {
                    HStack {
                        Text("Радиус размытия")
                        Slider(
                            value: Binding(
                                get: { Double(settings.backgroundBlurRadius) },
                                set: { settings.backgroundBlurRadius = Int($0.rounded()) }
                            ),
                            in: 1...25,
                            step: 1
                        )
                        Text("\(settings.backgroundBlurRadius)")
                            .font(.callout.monospacedDigit())
                            .foregroundStyle(.secondary)
                            .frame(width: 56, alignment: .trailing)
                    }
                }

                HStack {
                    Text("Затемнение")
                    Slider(
                        value: Binding(
                            get: { Double(settings.backgroundDimPercent) },
                            set: { settings.backgroundDimPercent = Int($0.rounded()) }
                        ),
                        in: 0...100,
                        step: 1
                    )
                    Text("\(settings.backgroundDimPercent)%")
                        .font(.callout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .frame(width: 56, alignment: .trailing)
                }
            }

            // 4. Папки чатов
            Section("Папки чатов") {
                Toggle("Компактные папки", isOn: $compactFolders)
                Toggle("Не показывать чаты папок во «Все чаты»", isOn: $excludeFolderChatsFromAll)
            }

            // 5. Изображения фона
            Section {
                if vm.isLoading {
                    HStack {
                        Spacer()
                        ProgressView()
                        Spacer()
                    }
                    .padding(.vertical, Theme.Spacing.md)
                } else {
                    BackgroundsGrid(viewModel: vm, settings: settings)
                }
            } header: {
                HStack {
                    Text("Изображения фона")
                    Spacer()
                    if vm.deleteMode {
                        Button("Готово") { vm.deleteMode = false }
                            .buttonStyle(.borderless)
                            .font(.callout)
                            .textCase(.none)
                    }
                }
            }
        }
    }
}
