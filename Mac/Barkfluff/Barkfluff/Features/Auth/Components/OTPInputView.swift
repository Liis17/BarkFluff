//
//  OTPInputView.swift
//  Barkfluff
//
//  Поле ввода OTP кода
//

import SwiftUI

/// Поле ввода OTP кода с отображением в виде отдельных ячеек
struct OTPInputView: View {
    @Binding var code: String

    let codeLength: Int = 6
    var isError: Bool = false
    var isDisabled: Bool = false
    var onComplete: ((String) -> Void)?

    @FocusState private var isFocused: Bool

    var body: some View {
        // Контейнер с фиксированным размером
        HStack(spacing: Theme.Spacing.sm) {
            ForEach(0..<codeLength, id: \.self) { index in
                digitCell(at: index)
            }
        }
        .background(
            // Скрытое поле для ввода (без визуальной обводки)
            TextField("", text: $code)
                .textFieldStyle(.plain) // Убираем стандартную обводку
                .opacity(0.01)
                .focused($isFocused)
                .disabled(isDisabled)
                .onChange(of: code) { _, newValue in
                    // Фильтруем только цифры
                    let digits = newValue.filter { $0.isNumber }
                    // Ограничиваем длину
                    code = String(digits.prefix(codeLength))
                    // Уведомляем если код полный
                    if code.count == codeLength {
                        onComplete?(code)
                    }
                }
        )
        .contentShape(Rectangle())
        .onTapGesture {
            isFocused = true
        }
        .onAppear {
            // Сначала сбрасываем фокус, потом захватываем
            isFocused = false
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                isFocused = true
            }
        }
        .onDisappear {
            // Сбрасываем фокус при уходе
            isFocused = false
        }
    }

    @ViewBuilder
    private func digitCell(at index: Int) -> some View {
        let digit = index < code.count ? String(code[code.index(code.startIndex, offsetBy: index)]) : ""
        let isCurrentCell = index == code.count
        let isLastCell = index == codeLength - 1 && code.count == codeLength

        Text(digit.isEmpty ? " " : digit)
            .font(.system(.title2, design: .monospaced))
            .fontWeight(.semibold)
            .frame(width: 44, height: 52)
            .background(
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .fill(Color(.textBackgroundColor))
            )
            .overlay(
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .strokeBorder(borderColor(isCurrent: isCurrentCell || isLastCell), lineWidth: 2)
            )
            .contentShape(Rectangle())
    }

    private func borderColor(isCurrent: Bool) -> Color {
        if isError {
            return .red
        } else if isCurrent && isFocused {
            return .accentColor
        } else {
            return .clear
        }
    }
}

/// Простой TextField для OTP кода (альтернатива)
struct OTPTextField: View {
    @Binding var code: String
    var isError: Bool = false
    var isDisabled: Bool = false
    var onComplete: ((String) -> Void)?

    var body: some View {
        TextField("000000", text: $code)
            .font(.system(.title2, design: .monospaced))
            .fontWeight(.semibold)
            .multilineTextAlignment(.center)
            .frame(width: 240, height: 52)
            .background(
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .fill(Color(.textBackgroundColor))
            )
            .overlay(
                RoundedRectangle(cornerRadius: Theme.Radius.md)
                    .strokeBorder(isError ? Color.red : Color.clear, lineWidth: 2)
            )
            .textFieldStyle(.plain)
            .disabled(isDisabled)
            .onChange(of: code) { _, newValue in
                let digits = newValue.filter { $0.isNumber }
                code = String(digits.prefix(6))
                if code.count == 6 {
                    onComplete?(code)
                }
            }
    }
}

#Preview {
    VStack(spacing: 30) {
        OTPInputView(code: .constant("123"))
        OTPInputView(code: .constant("123456"), isError: true)
        OTPInputView(code: .constant(""), isDisabled: true)

        OTPTextField(code: .constant("456789"))
    }
    .padding()
}
