import { FieldErrors } from "react-hook-form"
import { SpiSystemUserUpdateRequest } from "@/types/api.types"

interface UseSpiSystemUserUpdateFieldsProps {
  errors: FieldErrors<SpiSystemUserUpdateRequest>
  disabledFields?: string[]
}

export default function useSpiSystemUserUpdateFields(props: UseSpiSystemUserUpdateFieldsProps) {
  const { errors, disabledFields = [] } = props

  return {
    mobileNumber: {
      props: {
        id: "system-user-mobileNumber",
        name: "mobileNumber",
        heading: "Mobile Number",
        type: "phone" as const,
        placeholder: "Enter mobile number...",
        disabled: disabledFields.includes("mobileNumber"),
        error: errors.mobileNumber,
      },
      rules: { },
    },
  }
}
